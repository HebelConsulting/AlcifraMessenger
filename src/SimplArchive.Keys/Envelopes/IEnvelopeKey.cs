using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SimplArchive.Keys.Envelopes;

/// <summary>
/// The private half of one certificate's key, wherever it lives: what <see cref="EnvelopeOpener"/> asks of a key, and
/// nothing more. A PIV key reached by APDUs is one (<see cref="PivEnvelopeKey"/>); a key the operating system holds for
/// a smart card (a system token, which asks for the PIN itself) is another, implemented by the app that has it.
/// </summary>
/// <remarks>
/// The CMS work — finding the recipient, the X9.63 derivation, the key unwrap, the content decryption — stays in
/// <see cref="EnvelopeOpener"/> and <see cref="KeyAgreement"/>, ONE implementation for every kind of key. A key only
/// does the one private operation its recipient type needs.
/// </remarks>
public interface IEnvelopeKey
{
    /// <summary>The key's certificate (public, read without a PIN): which recipient of an envelope is this key's.</summary>
    X509Certificate2 Certificate { get; }

    /// <summary>
    /// Called once, after the envelope was found addressed to <see cref="Certificate"/> and before the first private
    /// operation: a PIV key verifies its PIN here, so a key the envelope is not addressed to never spends a PIN try. A key
    /// whose holder asks for the PIN itself does nothing.
    /// </summary>
    void Unlock();

    /// <summary>Elliptic-curve key agreement: the raw shared secret (the X coordinate) with the originator's point,
    /// given uncompressed (<c>04 || X || Y</c>).</summary>
    byte[] Agree(byte[] originatorPoint);

    /// <summary>RSA key transport: the content key, decrypted from <paramref name="encryptedKey"/> with the padding the
    /// recipient names (PKCS#1 v1.5 or OAEP).</summary>
    byte[] Decrypt(byte[] encryptedKey, RSAEncryptionPadding padding);
}
