using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SimplArchive.Messenger.Client;

/// <summary>Opens a member's sealed copy of a conversation key: what the app supplies, with the member's hardware key.</summary>
public interface IConversationKeyOpener
{
    /// <summary>The 32-byte key inside <paramref name="wrappedKey"/>, a CMS envelope addressed to the member's certificates.</summary>
    Task<byte[]> OpenAsync(byte[] wrappedKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Conversation keys: one random 256-bit key per epoch, and for each member a CMS envelope of it addressed to ALL of that
/// member's enrolled certificates, so any of their keys opens it.
/// </summary>
public static class ConversationKeys
{
    /// <summary>A fresh conversation key.</summary>
    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Seals <paramref name="key"/> to every one of <paramref name="certificates"/>: RSA key transport, or elliptic-curve
    /// key agreement with the SHA-256 X9.63 KDF and AES-256 key wrap (an originator key per recipient, on its own curve);
    /// AES-256-CBC content. The shape the key library's <c>EnvelopeOpener</c> opens.
    /// </summary>
    public static byte[] SealTo(byte[] key, IReadOnlyList<X509Certificate2> certificates)
    {
        if (certificates.Count == 0)
        {
            throw new ArgumentException("A key is sealed to at least one certificate.", nameof(certificates));
        }

        var parser = new Org.BouncyCastle.X509.X509CertificateParser();
        var generator = new Org.BouncyCastle.Cms.CmsEnvelopedDataGenerator();
        var random = new Org.BouncyCastle.Security.SecureRandom();
        foreach (var certificate in certificates)
        {
            var recipient = parser.ReadCertificate(certificate.RawData);
            if (recipient.GetPublicKey() is Org.BouncyCastle.Crypto.Parameters.ECPublicKeyParameters ec)
            {
                var originator = new Org.BouncyCastle.Crypto.Generators.ECKeyPairGenerator();
                originator.Init(new Org.BouncyCastle.Crypto.Parameters.ECKeyGenerationParameters(ec.Parameters, random));
                var pair = originator.GenerateKeyPair();
                generator.AddKeyAgreementRecipient(
                    Org.BouncyCastle.Cms.CmsEnvelopedGenerator.ECDHSha256Kdf, pair.Private, pair.Public, recipient,
                    Org.BouncyCastle.Cms.CmsEnvelopedGenerator.Aes256Wrap);
            }
            else
            {
                generator.AddKeyTransRecipient(recipient);
            }
        }

        return generator.Generate(
            new Org.BouncyCastle.Cms.CmsProcessableByteArray(key),
            new Org.BouncyCastle.Asn1.DerObjectIdentifier(Org.BouncyCastle.Cms.CmsEnvelopedGenerator.Aes256Cbc)).GetEncoded();
    }
}

/// <summary>
/// The conversation keys a session has opened, in memory only: each is opened once with the hardware key, then reused
/// until <see cref="Clear"/> (the app locks, the key leaves, the app leaves the screen).
/// </summary>
public sealed class KeyRing(IConversationKeyOpener opener)
{
    private readonly Dictionary<(Guid Conversation, int Epoch), byte[]> _keys = [];
    private readonly object _gate = new();

    /// <summary>The key of <paramref name="conversationId"/> at <paramref name="epoch"/>, opened from <paramref name="wrappedKey"/> if not yet held.</summary>
    public async Task<byte[]> KeyAsync(Guid conversationId, int epoch, byte[] wrappedKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_keys.TryGetValue((conversationId, epoch), out var held))
            {
                return held;
            }
        }

        var key = await opener.OpenAsync(wrappedKey, cancellationToken);
        if (key.Length != 32)
        {
            throw new CryptographicException("The opened conversation key is not 32 bytes.");
        }

        lock (_gate)
        {
            _keys[(conversationId, epoch)] = key;
        }

        return key;
    }

    /// <summary>Forgets every key, overwriting it first.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (var key in _keys.Values)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            _keys.Clear();
        }
    }
}
