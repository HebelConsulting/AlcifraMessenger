using System.Security.Cryptography.X509Certificates;

namespace SimplArchive.Keys.Piv;

/// <summary>The PIV key algorithms this app can use for decryption, by their PIV algorithm identifier.</summary>
public enum PivAlgorithm : byte
{
    /// <summary>RSA with a 1024-bit key.</summary>
    Rsa1024 = 0x06,
    /// <summary>RSA with a 2048-bit key.</summary>
    Rsa2048 = 0x07,
    /// <summary>RSA with a 3072-bit key.</summary>
    Rsa3072 = 0x05,
    /// <summary>RSA with a 4096-bit key.</summary>
    Rsa4096 = 0x16,
    /// <summary>Elliptic curve P-256.</summary>
    EccP256 = 0x11,
    /// <summary>Elliptic curve P-384.</summary>
    EccP384 = 0x14,
}

/// <summary>
/// The PIV application on a hardware key (NIST SP 800-73-4), reduced to what opening an envelope needs: select, verify
/// the PIN, read the key-management certificate, and the one private-key operation (RSA decipher or ECDH).
/// </summary>
/// <remarks>
/// <para>
/// <b>Chaining both ways.</b> A 2048-bit ciphertext is 256 bytes, more than one short APDU carries, so a command is sent
/// in chained pieces (CLA 0x10 on all but the last); and a long answer arrives in pieces the card announces with
/// <c>61xx</c>, collected with GET RESPONSE. Extended-length APDUs are avoided on purpose: NFC readers differ on them,
/// chaining works everywhere.
/// </para>
/// <para>The PIN goes to the card and nowhere else: it is not logged, kept or echoed.</para>
/// </remarks>
public sealed class PivSession(IApduTransport transport)
{
    /// <summary>The key-management slot, where a decryption key lives.</summary>
    public const byte KeyManagementSlot = 0x9D;

    private static readonly byte[] PivAid = [0xA0, 0x00, 0x00, 0x03, 0x08];

    /// <summary>Selects the PIV application on the key; the first command of every session.</summary>
    public void Select() => Expect("selecting PIV", Send(0x00, 0xA4, 0x04, 0x00, PivAid));

    /// <summary>Verifies the user PIN (6 to 8 characters), padded with 0xFF to 8 bytes as PIV requires.</summary>
    public void VerifyPin(string pin)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(pin);
        if (bytes.Length is < 6 or > 8)
        {
            throw new PivException("A PIV PIN has 6 to 8 characters.");
        }

        var padded = Enumerable.Repeat((byte)0xFF, 8).ToArray();
        bytes.CopyTo(padded, 0);
        var response = Send(0x00, 0x20, 0x00, 0x80, padded);
        var sw = StatusWord(response);
        if ((sw & 0xFFF0) == 0x63C0)
        {
            throw new PivWrongPinException(sw & 0x000F);
        }

        if (sw == 0x6983)
        {
            throw new PivWrongPinException(0);
        }

        Expect("the PIN", response);
    }

    /// <summary>The certificate in the key-management slot (9D), or null when the slot holds none.</summary>
    public X509Certificate2? ReadKeyManagementCertificate()
    {
        // GET DATA for object 5FC10B: answers 53 { 70 cert, 71 certinfo, FE lrc }.
        var response = Send(0x00, 0xCB, 0x3F, 0xFF, [0x5C, 0x03, 0x5F, 0xC1, 0x0B]);
        if (StatusWord(response) == 0x6A82)
        {
            return null;
        }

        var data = Expect("reading the certificate", response);
        return X509CertificateLoader.LoadCertificate(Value(Value(data, 0x53), 0x70));
    }

    /// <summary>The raw RSA private operation on a ciphertext block (no padding removed): what the card computes.</summary>
    public byte[] RsaDecipher(PivAlgorithm algorithm, byte[] ciphertext) =>
        GeneralAuthenticate(algorithm, 0x81, ciphertext, "decrypting");

    /// <summary>The ECDH shared secret (the X coordinate) with an uncompressed peer point <c>04||X||Y</c>.</summary>
    public byte[] EcdhSharedSecret(PivAlgorithm algorithm, byte[] peerPoint) =>
        GeneralAuthenticate(algorithm, 0x85, peerPoint, "agreeing a key");

    private byte[] GeneralAuthenticate(PivAlgorithm algorithm, byte challengeTag, byte[] input, string operation)
    {
        // 7C { 82 (empty: "respond"), 81|85 input }
        var inner = Concat(Tlv(0x82, []), Tlv(challengeTag, input));
        var answer = Expect(operation, Send(0x00, 0x87, (byte)algorithm, KeyManagementSlot, Tlv(0x7C, inner)));
        return Value(Value(answer, 0x7C), 0x82);
    }

    /// <summary>Sends a command, chained when its data exceeds a short APDU, and collects a chained answer.</summary>
    private byte[] Send(byte cla, byte ins, byte p1, byte p2, byte[] data)
    {
        byte[] response = [];
        for (var offset = 0; ; offset += 255)
        {
            var last = data.Length - offset <= 255;
            var chunk = data.AsSpan(offset, Math.Min(255, data.Length - offset)).ToArray();
            byte[] apdu = [(byte)(last ? cla : cla | 0x10), ins, p1, p2, (byte)chunk.Length, .. chunk];
            response = transport.Transmit(apdu);
            if (last)
            {
                break;
            }

            if (StatusWord(response) != 0x9000)
            {
                return response;
            }
        }

        var collected = new List<byte>(response[..^2]);
        while (response.Length >= 2 && response[^2] == 0x61)
        {
            response = transport.Transmit([0x00, 0xC0, 0x00, 0x00, response[^1]]);
            collected.AddRange(response[..^2]);
        }

        return [.. collected, response[^2], response[^1]];
    }

    private static byte[] Expect(string operation, byte[] response) =>
        StatusWord(response) == 0x9000 ? response[..^2] : throw new PivStatusException(operation, StatusWord(response));

    private static ushort StatusWord(byte[] response) =>
        response.Length < 2 ? (ushort)0 : (ushort)((response[^2] << 8) | response[^1]);

    /// <summary>
    /// The value of the first top-level element with a one-byte <paramref name="tag"/>. PIV data is BER-TLV, not ASN.1 to
    /// be decoded by type: 0x70 and 0x7C carry the "constructed" bit, so an ASN.1 reader would mangle a certificate.
    /// </summary>
    internal static byte[] Value(byte[] data, byte tag)
    {
        for (var at = 0; at < data.Length;)
        {
            var t = data[at++];
            int length = data[at++];
            if (length == 0x81)
            {
                length = data[at++];
            }
            else if (length == 0x82)
            {
                length = (data[at] << 8) | data[at + 1];
                at += 2;
            }

            if (t == tag)
            {
                return data.AsSpan(at, length).ToArray();
            }

            at += length;
        }

        throw new PivException($"The key's answer has no element with tag {tag:X2}.");
    }

    /// <summary>BER-TLV with definite length (short, 0x81 or 0x82 form).</summary>
    internal static byte[] Tlv(byte tag, byte[] value)
    {
        byte[] length = value.Length switch
        {
            < 0x80 => [(byte)value.Length],
            <= 0xFF => [0x81, (byte)value.Length],
            _ => [0x82, (byte)(value.Length >> 8), (byte)value.Length],
        };
        return [tag, .. length, .. value];
    }

    private static byte[] Concat(byte[] a, byte[] b) => [.. a, .. b];
}
