using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SimplArchive.Keys.Piv;

namespace SimplArchive.Keys.Envelopes;

/// <summary>
/// The key in a PIV key's slot 9D (key management), reached by APDUs: the certificate read without the PIN, the PIN
/// verified on <see cref="Unlock"/>, and the private operation done on the key.
/// </summary>
public sealed class PivEnvelopeKey : IEnvelopeKey, IDisposable
{
    private readonly PivSession _session;
    private readonly string _pin;

    /// <summary>Selects PIV and reads the slot's certificate.</summary>
    /// <exception cref="EnvelopeException">The slot holds no certificate.</exception>
    public PivEnvelopeKey(IApduTransport transport, string pin)
    {
        _session = new PivSession(transport);
        _session.Select();
        _pin = pin;
        Certificate = _session.ReadKeyManagementCertificate()
            ?? throw new EnvelopeException("this key holds no certificate in its key-management slot (9D)");
    }

    /// <inheritdoc/>
    public X509Certificate2 Certificate { get; }

    /// <summary>Verifies the PIN on the key.</summary>
    /// <exception cref="PivException">The PIN was wrong, or the key refused.</exception>
    public void Unlock() => _session.VerifyPin(_pin);

    /// <inheritdoc/>
    public byte[] Agree(byte[] originatorPoint)
    {
        using var publicKey = Certificate.GetECDiffieHellmanPublicKey()
            ?? throw new EnvelopeException("the key's certificate is not an elliptic-curve certificate");
        var algorithm = publicKey.KeySize switch
        {
            256 => PivAlgorithm.EccP256,
            384 => PivAlgorithm.EccP384,
            _ => throw new EnvelopeException($"a P-{publicKey.KeySize} key is not one a PIV key holds"),
        };
        return _session.EcdhSharedSecret(algorithm, originatorPoint);
    }

    /// <inheritdoc/>
    public byte[] Decrypt(byte[] encryptedKey, RSAEncryptionPadding padding)
    {
        using var publicKey = Certificate.GetRSAPublicKey()
            ?? throw new EnvelopeException("the key's certificate is not an RSA certificate");
        using var card = new CardRsa(_session, publicKey);
        return card.Decrypt(encryptedKey, padding);
    }

    /// <summary>Releases the certificate read from the key.</summary>
    public void Dispose() => Certificate.Dispose();
}
