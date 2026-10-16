using System.Formats.Asn1;
using System.Security.Cryptography;

namespace SimplArchive.Keys.Envelopes;

/// <summary>
/// Opening an elliptic-curve (key-agreement) recipient of a CMS envelope, which the platform's <c>EnvelopedCms</c> cannot do
/// with an external key outside Windows: read the originator's point, have the hardware key agree the shared secret,
/// derive the key-encryption key (X9.63), unwrap the content key (RFC 3394), decrypt the content.
/// </summary>
/// <remarks>
/// Lessons kept from the first implementation: the KDF hash comes from the scheme OID (a common default is SHA-1, which
/// silently mismatches a SHA-256 scheme); the ECC-CMS-SharedInfo tags are [0] and [2]; and both spellings of keyInfo
/// (absent parameters, explicit NULL) are tried, since RFC 3394's integrity value makes a wrong one fail instead of
/// decrypting to noise.
/// </remarks>
public static class KeyAgreement
{
    private static readonly Dictionary<string, HashAlgorithmName> KdfSchemes = new(StringComparer.Ordinal)
    {
        ["1.3.133.16.840.63.0.2"] = HashAlgorithmName.SHA1,
        ["1.3.132.1.11.1"] = HashAlgorithmName.SHA256,
        ["1.3.132.1.11.2"] = HashAlgorithmName.SHA384,
        ["1.3.132.1.11.3"] = HashAlgorithmName.SHA512,
    };

    private static readonly Dictionary<string, int> KeyWraps = new(StringComparer.Ordinal)
    {
        ["2.16.840.1.101.3.4.1.5"] = 16,
        ["2.16.840.1.101.3.4.1.25"] = 24,
        ["2.16.840.1.101.3.4.1.45"] = 32,
    };

    private static readonly byte[] DefaultIv = [0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6];

    /// <summary>The content key of the key-agreement recipient at <paramref name="recipientIndex"/>.</summary>
    public static byte[] ContentKey(byte[] cms, int recipientIndex, byte[] encryptedKey, Func<byte[], byte[]> agree)
    {
        var (point, ukm, kdfOid, wrapOid) = ReadKeyAgreement(cms, recipientIndex);
        var hash = KdfSchemes.TryGetValue(kdfOid, out var h) ? h : throw new EnvelopeException($"the key derivation {kdfOid} is not supported");
        var kekLength = KeyWraps.TryGetValue(wrapOid, out var n) ? n : throw new EnvelopeException($"the key wrap {wrapOid} is not supported");

        var shared = agree(point);
        foreach (var nullParameters in new[] { false, true })
        {
            var kek = Kdf(shared, SharedInfo(wrapOid, ukm, kekLength * 8, nullParameters), kekLength, hash);
            if (Unwrap(kek, encryptedKey) is { } contentKey)
            {
                return contentKey;
            }
        }

        throw new EnvelopeException("the content key did not unwrap: this key is not the one the envelope was made for");
    }

    /// <summary>X9.63 KDF: Hash(Z || counter (big-endian) || SharedInfo), concatenated to length.</summary>
    internal static byte[] Kdf(byte[] z, byte[] sharedInfo, int length, HashAlgorithmName hash)
    {
        var derived = new List<byte>(length);
        for (uint counter = 1; derived.Count < length; counter++)
        {
            byte[] block = [.. z, (byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter, .. sharedInfo];
            derived.AddRange(CryptographicOperations.HashData(hash, block));
        }

        return [.. derived.Take(length)];
    }

    /// <summary>RFC 3394 AES key unwrap; null when the integrity check fails (a wrong key-encryption key).</summary>
    internal static byte[]? Unwrap(byte[] kek, byte[] wrapped)
    {
        if (wrapped.Length < 24 || wrapped.Length % 8 != 0)
        {
            return null;
        }

        using var aes = Aes.Create();
        aes.Key = kek;
        var n = (wrapped.Length / 8) - 1;
        var a = wrapped[..8];
        var r = Enumerable.Range(0, n).Select(i => wrapped[(8 * (i + 1))..(8 * (i + 2))]).ToArray();
        var buffer = new byte[16];
        for (var j = 5; j >= 0; j--)
        {
            for (var i = n; i >= 1; i--)
            {
                var t = (uint)((n * j) + i);
                a.CopyTo(buffer, 0);
                buffer[7] ^= (byte)t;
                buffer[6] ^= (byte)(t >> 8);
                buffer[5] ^= (byte)(t >> 16);
                buffer[4] ^= (byte)(t >> 24);
                r[i - 1].CopyTo(buffer, 8);
                var decrypted = aes.DecryptEcb(buffer, PaddingMode.None);
                a = decrypted[..8];
                r[i - 1] = decrypted[8..];
            }
        }

        return CryptographicOperations.FixedTimeEquals(a, DefaultIv) ? [.. r.SelectMany(b => b)] : null;
    }

    /// <summary>ECC-CMS-SharedInfo (RFC 5753 §7.2): keyInfo, [0] UKM when present, [2] KEK length in bits.</summary>
    internal static byte[] SharedInfo(string keyWrapOid, byte[]? ukm, int kekBits, bool nullParameters)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(keyWrapOid);
                if (nullParameters)
                {
                    writer.WriteNull();
                }
            }

            if (ukm is { Length: > 0 })
            {
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                {
                    writer.WriteOctetString(ukm);
                }
            }

            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true)))
            {
                writer.WriteOctetString([(byte)(kekBits >> 24), (byte)(kekBits >> 16), (byte)(kekBits >> 8), (byte)kekBits]);
            }
        }

        return writer.Encode();
    }

    /// <summary>The originator's point, UKM, KDF scheme and key-wrap OIDs of the recipient at an index (read raw: .NET
    /// does not surface the UKM).</summary>
    internal static (byte[] Point, byte[]? Ukm, string KdfOid, string WrapOid) ReadKeyAgreement(byte[] cms, int recipientIndex)
    {
        try
        {
            var recipients = EnvelopedData(cms).ReadSetOf();
            for (var i = 0; i < recipientIndex; i++)
            {
                recipients.ReadEncodedValue();
            }

            var recipient = recipients.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
            recipient.ReadInteger();
            var originatorKey = recipient
                .ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true))
                .ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
            originatorKey.ReadSequence();
            var point = originatorKey.ReadBitString(out _);

            byte[]? ukm = null;
            if (recipient.PeekTag() == new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true))
            {
                ukm = recipient.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)).ReadOctetString();
            }

            var keyEncryption = recipient.ReadSequence();
            var kdf = keyEncryption.ReadObjectIdentifier();
            var wrap = keyEncryption.ReadSequence().ReadObjectIdentifier();
            return (point, ukm, kdf, wrap);
        }
        catch (AsnContentException e)
        {
            throw new EnvelopeException("its key-agreement structure could not be read", e);
        }
    }

    /// <summary>The content cipher's OID and IV.</summary>
    internal static (string CipherOid, byte[] Iv) ReadContentCipher(byte[] cms)
    {
        try
        {
            var enveloped = EnvelopedData(cms);
            enveloped.ReadSetOf();
            var encryptedContentInfo = enveloped.ReadSequence();
            encryptedContentInfo.ReadObjectIdentifier();
            var algorithm = encryptedContentInfo.ReadSequence();
            return (algorithm.ReadObjectIdentifier(), algorithm.ReadOctetString());
        }
        catch (AsnContentException e)
        {
            throw new EnvelopeException("its encrypted structure could not be read", e);
        }
    }

    private static AsnReader EnvelopedData(byte[] cms)
    {
        var contentInfo = new AsnReader(cms, AsnEncodingRules.BER).ReadSequence();
        contentInfo.ReadObjectIdentifier();
        var enveloped = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadSequence();
        enveloped.ReadInteger();
        if (enveloped.PeekTag().TagClass == TagClass.ContextSpecific)
        {
            enveloped.ReadEncodedValue();   // originatorInfo
        }

        return enveloped;
    }
}
