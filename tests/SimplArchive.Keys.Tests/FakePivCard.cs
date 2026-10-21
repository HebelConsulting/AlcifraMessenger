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
internal sealed class FakePivCard(
    X509Certificate2 certificate, AsymmetricAlgorithm privateKey, string pin = "123456", byte[]? version = null, uint? serial = null)
    : IApduTransport
{
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
        return ins switch
        {
            0xA4 => [0x90, 0x00],
            0x20 => Verify(data),
            0xCB => Answer(PivSession.Tlv(0x53, [.. PivSession.Tlv(0x70, certificate.RawData), .. PivSession.Tlv(0x71, [0]), 0xFE, 0x00])),
            0xFD when version is not null => [.. version, 0x90, 0x00],
            0xF8 when serial is { } number => [(byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number, 0x90, 0x00],
            0x87 => _verified ? Answer(PivSession.Tlv(0x7C, PivSession.Tlv(0x82, Operate(apdu[2], data)))) : [0x69, 0x82],
            _ => [0x6D, 0x00],
        };
    }

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
