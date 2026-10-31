using System.Security.Cryptography;

namespace Alcifra.Keys.Envelopes;

/// <summary>
/// An <see cref="IEnvelopeKey"/> as an <see cref="RSA"/>, so the platform's own <c>EnvelopedCms.Decrypt(recipient, key)</c>
/// opens a key-transport recipient with it unchanged: the decryption is the key's, everything else the platform's.
/// </summary>
internal sealed class EnvelopeKeyRsa : RSA
{
    private readonly IEnvelopeKey _key;
    private readonly RSAParameters _public;

    public EnvelopeKeyRsa(IEnvelopeKey key, RSA publicKey)
    {
        _key = key;
        _public = publicKey.ExportParameters(false);
        KeySizeValue = publicKey.KeySize;
        LegalKeySizesValue = [new KeySizes(1024, 16384, 8)];
    }

    public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding) => _key.Decrypt(data, padding);

    public override RSAParameters ExportParameters(bool includePrivateParameters) =>
        includePrivateParameters ? throw new CryptographicException("The private key never leaves the key that holds it.") : _public;

    public override void ImportParameters(RSAParameters parameters) =>
        throw new NotSupportedException("The key that holds the private half cannot be replaced from here.");
}
