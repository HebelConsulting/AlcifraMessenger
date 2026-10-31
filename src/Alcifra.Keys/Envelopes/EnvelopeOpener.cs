using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using Alcifra.Keys.Piv;

namespace Alcifra.Keys.Envelopes;

/// <summary>What came out of an envelope, and how it was opened.</summary>
public sealed record OpenedEnvelope(byte[] Content, string Scheme, string Certificate)
{
    /// <summary>SHA-256 of the content, lowercase hex: what a test compares with the original file.</summary>
    public string Sha256 => Convert.ToHexStringLower(SHA256.HashData(Content));
}

/// <summary>
/// Opens a CMS envelope with a key: find the recipient addressed to the key's certificate (on a PIV key, slot 9D, key
/// management), and open it by its type. The key is an <see cref="IEnvelopeKey"/>; the CMS work is all here.
/// </summary>
/// <remarks>
/// <para>
/// <b>RSA key transport</b> goes through the platform's <c>EnvelopedCms.Decrypt</c> with the key as an <see cref="RSA"/>: the key does
/// the private operation, the platform the rest. <b>Elliptic-curve key agreement</b> goes through <see cref="KeyAgreement"/>,
/// because the platform cannot open such a recipient with an external key outside Windows.
/// </para>
/// <para>An envelope addressed to several certificates (one person's several keys) is fine: only this key's recipient is used.</para>
/// </remarks>
public static class EnvelopeOpener
{
    private const string AesCbcOid = "2.16.840.1.101.3.4.1.42";

    /// <summary>Opens <paramref name="envelope"/> with the key behind <paramref name="transport"/>.</summary>
    /// <param name="envelope">The DER-encoded CMS EnvelopedData.</param>
    /// <param name="transport">A connection to the key, from the app's <see cref="IKeyConnector"/>.</param>
    /// <param name="pin">The key's PIN; never stored here.</param>
    /// <returns>The content, and how and for which certificate it was opened.</returns>
    /// <exception cref="PivException">The key refused, or the PIN was wrong.</exception>
    /// <exception cref="EnvelopeException">The envelope is not addressed to this key, or cannot be opened.</exception>
    public static OpenedEnvelope Open(byte[] envelope, IApduTransport transport, string pin)
    {
        using var key = new PivEnvelopeKey(transport, pin);
        return Open(envelope, key);
    }

    /// <summary>
    /// Opens <paramref name="envelope"/> with <paramref name="key"/>, wherever that key lives: the recipient addressed to
    /// its certificate is found first, <see cref="IEnvelopeKey.Unlock"/> is called only then, and the key does the one
    /// private operation its recipient type needs. Every other step is done here, the same for every kind of key.
    /// </summary>
    /// <exception cref="EnvelopeException">The envelope is not addressed to this key, or cannot be opened.</exception>
    public static OpenedEnvelope Open(byte[] envelope, IEnvelopeKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var certificate = key.Certificate;
        var cms = new EnvelopedCms();
        try
        {
            cms.Decode(envelope);
        }
        catch (CryptographicException e)
        {
            throw new EnvelopeException("the file is not a CMS envelope", e);
        }

        var index = RecipientIndexOf(cms, certificate);
        if (index < 0)
        {
            throw new EnvelopeException($"it is not addressed to this key's certificate ({certificate.Subject})");
        }

        key.Unlock();
        var recipient = cms.RecipientInfos[index];
        return recipient switch
        {
            KeyTransRecipientInfo => OpenKeyTransport(cms, recipient, key),
            KeyAgreeRecipientInfo => OpenKeyAgreement(envelope, index, recipient, key),
            _ => throw new EnvelopeException($"its recipient type {recipient.Type} is not supported"),
        };
    }

    private static OpenedEnvelope OpenKeyTransport(EnvelopedCms cms, RecipientInfo recipient, IEnvelopeKey key)
    {
        using var publicKey = key.Certificate.GetRSAPublicKey() ?? throw new EnvelopeException("the key's certificate is not an RSA certificate");
        using var keyAsRsa = new EnvelopeKeyRsa(key, publicKey);
        try
        {
            cms.Decrypt(recipient, keyAsRsa);
        }
        catch (CryptographicException e)
        {
            throw new EnvelopeException("the content key did not decrypt with this key", e);
        }

        var padding = recipient.KeyEncryptionAlgorithm.Oid.Value switch
        {
            "1.2.840.113549.1.1.1" => "PKCS#1 v1.5",
            "1.2.840.113549.1.1.7" => "OAEP",
            var other => other,
        };
        return new OpenedEnvelope(cms.ContentInfo.Content, $"RSA key transport ({padding})", key.Certificate.Subject);
    }

    private static OpenedEnvelope OpenKeyAgreement(byte[] envelope, int index, RecipientInfo recipient, IEnvelopeKey key)
    {
        using var publicKey = key.Certificate.GetECDiffieHellmanPublicKey() ?? throw new EnvelopeException("the key's certificate is not an elliptic-curve certificate");
        var size = publicKey.KeySize;

        var contentKey = KeyAgreement.ContentKey(envelope, index, recipient.EncryptedKey, key.Agree);
        var (cipherOid, iv) = KeyAgreement.ReadContentCipher(envelope);
        if (cipherOid != AesCbcOid)
        {
            throw new EnvelopeException($"its content cipher {cipherOid} is not supported");
        }

        using var aes = Aes.Create();
        aes.Key = contentKey;
        var encrypted = EncryptedContent(envelope);
        return new OpenedEnvelope(aes.DecryptCbc(encrypted, iv), $"EC key agreement (P-{size})", key.Certificate.Subject);
    }

    /// <summary>The encrypted content octets, read raw (the platform hides them until it decrypts).</summary>
    private static byte[] EncryptedContent(byte[] envelope)
    {
        var reader = new System.Formats.Asn1.AsnReader(envelope, System.Formats.Asn1.AsnEncodingRules.BER).ReadSequence();
        reader.ReadObjectIdentifier();
        var enveloped = reader.ReadSequence(new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, 0, true)).ReadSequence();
        enveloped.ReadInteger();
        if (enveloped.PeekTag().TagClass == System.Formats.Asn1.TagClass.ContextSpecific)
        {
            enveloped.ReadEncodedValue();
        }

        enveloped.ReadSetOf();
        var info = enveloped.ReadSequence();
        info.ReadObjectIdentifier();
        info.ReadSequence();
        return info.ReadOctetString(new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, 0));
    }

    /// <summary>The recipient addressed to <paramref name="certificate"/> by issuer and serial or subject key identifier, or -1.</summary>
    internal static int RecipientIndexOf(EnvelopedCms cms, X509Certificate2 certificate)
    {
        var ski = certificate.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault()?.SubjectKeyIdentifier;
        for (var i = 0; i < cms.RecipientInfos.Count; i++)
        {
            var id = cms.RecipientInfos[i].RecipientIdentifier;
            var matches = id.Value switch
            {
                X509IssuerSerial issuerSerial => issuerSerial.IssuerName == certificate.Issuer
                    && string.Equals(issuerSerial.SerialNumber, certificate.SerialNumber, StringComparison.OrdinalIgnoreCase),
                string subjectKeyIdentifier => ski is not null && string.Equals(subjectKeyIdentifier, ski, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
            if (matches)
            {
                return i;
            }
        }

        return -1;
    }
}
