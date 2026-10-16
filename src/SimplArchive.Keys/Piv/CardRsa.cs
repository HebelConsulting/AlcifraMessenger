using System.Security.Cryptography;

namespace SimplArchive.Keys.Piv;

/// <summary>
/// An RSA key whose private half is on the hardware key: an <see cref="RSA"/>, so the platform's own
/// <c>EnvelopedCms.Decrypt(recipient, key)</c> opens a key-transport envelope with it unchanged.
/// </summary>
/// <remarks>
/// The card computes only the raw private operation; the padding (PKCS#1 v1.5 or OAEP) is removed here. The checks run
/// over the whole block and fail with one exception type whatever went wrong, so a malformed block reveals nothing
/// about where it differed.
/// </remarks>
public sealed class CardRsa : RSA
{
    private readonly PivSession _session;
    private readonly PivAlgorithm _algorithm;
    private readonly RSAParameters _public;

    /// <summary>An RSA key whose private operation runs on the key behind <paramref name="session"/>.</summary>
    /// <param name="session">A selected PIV session whose PIN has been verified.</param>
    /// <param name="publicKey">The public half, from the slot's certificate; its size picks the PIV algorithm.</param>
    /// <exception cref="PivException">The key size is not one a PIV key holds.</exception>
    public CardRsa(PivSession session, RSA publicKey)
    {
        _session = session;
        _public = publicKey.ExportParameters(false);
        KeySizeValue = publicKey.KeySize;
        LegalKeySizesValue = [new KeySizes(1024, 4096, 1024)];
        _algorithm = KeySizeValue switch
        {
            1024 => PivAlgorithm.Rsa1024,
            2048 => PivAlgorithm.Rsa2048,
            3072 => PivAlgorithm.Rsa3072,
            4096 => PivAlgorithm.Rsa4096,
            _ => throw new PivException($"An RSA-{KeySizeValue} key is not one a PIV key holds."),
        };
    }

    /// <summary>Decrypts on the hardware key and removes the padding here, PKCS#1 v1.5 or OAEP.</summary>
    public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding)
    {
        var block = _session.RsaDecipher(_algorithm, LeftPad(data, KeySizeValue / 8));
        return padding.Mode switch
        {
            RSAEncryptionPaddingMode.Pkcs1 => RsaPadding.RemovePkcs1(block),
            RSAEncryptionPaddingMode.Oaep => RsaPadding.RemoveOaep(block, padding.OaepHashAlgorithm),
            _ => throw new CryptographicException($"The {padding.Mode} padding is not supported."),
        };
    }

    /// <summary>The public parameters; asking for the private ones throws, because they never leave the key.</summary>
    public override RSAParameters ExportParameters(bool includePrivateParameters) =>
        includePrivateParameters ? throw new CryptographicException("The private key never leaves the hardware key.") : _public;

    /// <summary>Not supported: the key on the hardware cannot be replaced from here.</summary>
    public override void ImportParameters(RSAParameters parameters) =>
        throw new NotSupportedException("A hardware key's RSA key cannot be replaced from here.");

    private static byte[] LeftPad(byte[] data, int length) =>
        data.Length >= length ? data : [.. new byte[length - data.Length], .. data];
}

/// <summary>Removing RSA encryption padding from a raw private-operation block (RFC 8017 §7.1.2 and §7.2.2).</summary>
internal static class RsaPadding
{
    public static byte[] RemovePkcs1(byte[] block)
    {
        // 00 02 PS (at least 8 non-zero bytes) 00 M
        var separator = Array.IndexOf(block, (byte)0, 2);
        var valid = block.Length > 11 && block[0] == 0x00 && block[1] == 0x02 && separator >= 10;
        return valid ? block[(separator + 1)..] : throw new CryptographicException("The decrypted key block is not valid.");
    }

    public static byte[] RemoveOaep(byte[] block, HashAlgorithmName hash)
    {
        var hLen = CryptographicOperations.HashData(hash, []).Length;
        if (block.Length < (2 * hLen) + 2 || block[0] != 0)
        {
            throw new CryptographicException("The decrypted key block is not valid.");
        }

        var maskedSeed = block[1..(1 + hLen)];
        var maskedDb = block[(1 + hLen)..];
        var seed = Xor(maskedSeed, Mgf1(maskedDb, hLen, hash));
        var db = Xor(maskedDb, Mgf1(seed, maskedDb.Length, hash));

        var labelHash = CryptographicOperations.HashData(hash, []);
        var labelMatches = CryptographicOperations.FixedTimeEquals(db[..hLen], labelHash);
        var separator = Array.IndexOf(db, (byte)1, hLen);
        var paddingIsZero = separator > 0 && db[hLen..separator].All(b => b == 0);
        return labelMatches && paddingIsZero ? db[(separator + 1)..] : throw new CryptographicException("The decrypted key block is not valid.");
    }

    private static byte[] Mgf1(byte[] seed, int length, HashAlgorithmName hash)
    {
        var output = new List<byte>(length);
        for (uint counter = 0; output.Count < length; counter++)
        {
            byte[] input = [.. seed, (byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter];
            output.AddRange(CryptographicOperations.HashData(hash, input));
        }

        return [.. output.Take(length)];
    }

    private static byte[] Xor(byte[] a, byte[] b) => [.. a.Zip(b, (x, y) => (byte)(x ^ y))];
}
