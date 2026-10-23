using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SimplArchive.Keys.Piv;

namespace SimplArchive.Keys.Tests;

/// <summary>
/// A PIV card in memory, holding one software key in slot 9D, answering the APDUs the app sends: SELECT, VERIFY, GET DATA
/// for the certificate, GENERAL AUTHENTICATE (RSA decipher or ECDH). It CHAINS both ways, as a real card does: it accepts
/// a command in CLA 0x10 pieces, and hands out any answer over 256 bytes in 61xx pieces, so the session's chaining is
/// exercised, not assumed.
/// </summary>
/// <param name="version">What GET VERSION answers (three bytes), or null for a key without it (6D00).</param>
/// <param name="serial">What GET SERIAL answers, or null for a key without it (6D00).</param>
/// <param name="slotAttestation">What ATTEST for slot 9D answers (a certificate's DER), or null for none.</param>
/// <param name="attestationCertificate">What GET DATA 5FFF01 (slot F9) answers, or null for none (6A82).</param>
/// <param name="attestRefusal">The status word ATTEST answers instead: 6D00 (firmware without it), 6A80 (an imported key).</param>
/// <remarks>Like a real key it answers 6D00 to every PIV command until PIV is SELECTed (the 5Ci over Lightning did).</remarks>
internal sealed class FakePivCard(
    X509Certificate2 certificate, AsymmetricAlgorithm privateKey, string pin = "123456", byte[]? version = null, uint? serial = null,
    byte[]? slotAttestation = null, byte[]? attestationCertificate = null, ushort? attestRefusal = null)
    : IApduTransport
{
    private static readonly byte[] KeyManagementObject = [0x5C, 0x03, 0x5F, 0xC1, 0x0B];
    private static readonly byte[] AttestationObject = [0x5C, 0x03, 0x5F, 0xFF, 0x01];
    private bool _selected;

    private readonly List<byte> _chained = [];
    private byte[] _pending = [];
    private bool _verified;

    public int PinAttempts { get; private set; }

    public byte[] Transmit(byte[] apdu)
    {
        var (cla, ins) = (apdu[0], apdu[1]);
        var data = apdu.Length > 5 ? apdu[5..(5 + apdu[4])] : [];
        if (ins == 0xC0)
        {
            return Next();
        }

        if ((cla & 0x10) != 0)
        {
            _chained.AddRange(data);
            return [0x90, 0x00];
        }

        data = [.. _chained, .. data];
        _chained.Clear();
        if (ins == 0xA4)
        {
            _selected = data.AsSpan().SequenceEqual((byte[])[0xA0, 0x00, 0x00, 0x03, 0x08]);
            return _selected ? [0x90, 0x00] : [0x6A, 0x82];
        }

        if (!_selected)
        {
            return [0x6D, 0x00];
        }

        return ins switch
        {
            0x20 => Verify(data),
            0xCB when data.AsSpan().SequenceEqual(KeyManagementObject) => CertificateObject(certificate.RawData),
            0xCB when data.AsSpan().SequenceEqual(AttestationObject) && attestationCertificate is not null => CertificateObject(attestationCertificate),
            0xCB => [0x6A, 0x82],
            0xF9 when attestRefusal is { } refusal => [(byte)(refusal >> 8), (byte)refusal],
            0xF9 when apdu[2] == PivSession.KeyManagementSlot && slotAttestation is not null => Answer(slotAttestation),
            0xF9 => [0x6A, 0x80],
            0xFD when version is not null => [.. version, 0x90, 0x00],
            0xF8 when serial is { } number => [(byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number, 0x90, 0x00],
            0x87 => _verified ? Answer(PivSession.Tlv(0x7C, PivSession.Tlv(0x82, Operate(apdu[2], data)))) : [0x69, 0x82],
            _ => [0x6D, 0x00],
        };
    }

    private byte[] CertificateObject(byte[] der) =>
        Answer(PivSession.Tlv(0x53, [.. PivSession.Tlv(0x70, der), .. PivSession.Tlv(0x71, [0]), 0xFE, 0x00]));

    private byte[] Verify(byte[] padded)
    {
        PinAttempts++;
        _verified = System.Text.Encoding.ASCII.GetString([.. padded.TakeWhile(b => b != 0xFF)]) == pin;
        return _verified ? [0x90, 0x00] : [0x63, 0xC2];
    }

    private byte[] Operate(byte algorithm, byte[] template)
    {
        var inner = PivSession.Value(template, 0x7C);
        if (privateKey is RSA rsa)
        {
            Assert.True(algorithm == (byte)PivAlgorithm.Rsa2048, "the session must name RSA-2048 for a 2048-bit key");
            var p = rsa.ExportParameters(true);
            var c = new BigInteger(PivSession.Value(inner, 0x81), isUnsigned: true, isBigEndian: true);
            var m = BigInteger.ModPow(c, new BigInteger(p.D, true, true), new BigInteger(p.Modulus, true, true));
            var raw = m.ToByteArray(isUnsigned: true, isBigEndian: true);
            return [.. new byte[p.Modulus!.Length - raw.Length], .. raw];
        }

        Assert.True(algorithm == (byte)PivAlgorithm.EccP256, "the session must name ECC P-256 for a P-256 key");
        var ecdh = (ECDiffieHellman)privateKey;
        var point = PivSession.Value(inner, 0x85);
        using var peer = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point[1..33], Y = point[33..] },
        });
        return ecdh.DeriveRawSecretAgreement(peer.PublicKey);
    }

    private byte[] Answer(byte[] data)
    {
        _pending = data;
        return Next();
    }

    private byte[] Next()
    {
        var take = Math.Min(256, _pending.Length);
        var chunk = _pending[..take];
        _pending = _pending[take..];
        return _pending.Length == 0 ? [.. chunk, 0x90, 0x00] : [.. chunk, 0x61, (byte)Math.Min(255, _pending.Length)];
    }

    public void Dispose()
    {
    }
}
