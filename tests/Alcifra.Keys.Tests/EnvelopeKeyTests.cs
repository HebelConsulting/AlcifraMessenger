using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Alcifra.Keys.Envelopes;

namespace Alcifra.Keys.Tests;

// The same openssl-made envelopes, opened with a key that is NOT reached by APDUs: an IEnvelopeKey over a software key,
// standing in for a key the operating system holds for a smart card (the phone's AutoFill, Mobile #161). The CMS work is
// the opener's either way; the key only does its one private operation, and only after the addressee check.
public sealed class EnvelopeKeyTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly byte[] Content = File.ReadAllBytes(Path.Combine(Fixtures, "content.txt"));

    private static byte[] Envelope(string name) => File.ReadAllBytes(Path.Combine(Fixtures, name));

    private static SoftwareKey Key(string name) =>
        new(X509Certificate2.CreateFromPemFile(Path.Combine(Fixtures, $"{name}.pem"), Path.Combine(Fixtures, $"{name}.key")));

    [Theory]
    [InlineData("both-pkcs1.p7m", "PKCS#1 v1.5")]
    [InlineData("both-oaep.p7m", "OAEP")]
    public void An_rsa_key_opens_its_key_transport_recipient_through_its_decrypt(string envelope, string padding)
    {
        using var key = Key("test-rsa");

        var opened = EnvelopeOpener.Open(Envelope(envelope), key);

        Assert.Equal(Content, opened.Content);
        Assert.Equal($"RSA key transport ({padding})", opened.Scheme);
        Assert.Equal(1, key.Unlocks);
        Assert.Equal(1, key.Decrypts);
        Assert.Equal(0, key.Agreements);
    }

    [Fact]
    public void A_p256_key_opens_its_key_agreement_recipient_through_its_agree()
    {
        using var key = Key("test-ec");

        var opened = EnvelopeOpener.Open(Envelope("both-pkcs1.p7m"), key);

        Assert.Equal(Content, opened.Content);
        Assert.Equal("EC key agreement (P-256)", opened.Scheme);
        Assert.Equal(1, key.Unlocks);
        Assert.Equal(1, key.Agreements);
        Assert.Equal(0, key.Decrypts);
    }

    [Fact]
    public void A_key_the_envelope_is_not_addressed_to_is_never_unlocked()
    {
        using var stranger = new SoftwareKey(SelfSigned());

        Assert.Throws<EnvelopeException>(() => EnvelopeOpener.Open(Envelope("both-pkcs1.p7m"), stranger));
        Assert.Equal(0, stranger.Unlocks);
    }

    [Fact]
    public void A_key_whose_holder_refuses_ends_the_open_with_the_holders_exception()
    {
        using var key = Key("test-ec");
        key.Refusal = new OperationCanceledException("the PIN dialog was cancelled");

        Assert.Throws<OperationCanceledException>(() => EnvelopeOpener.Open(Envelope("both-pkcs1.p7m"), key));
    }

    private static X509Certificate2 SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        return new CertificateRequest("CN=Stranger", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    // A key whose private half is a software key, counting what it was asked to do.
    private sealed class SoftwareKey(X509Certificate2 withPrivateKey) : IEnvelopeKey, IDisposable
    {
        public X509Certificate2 Certificate { get; } = withPrivateKey;

        public int Unlocks { get; private set; }

        public int Agreements { get; private set; }

        public int Decrypts { get; private set; }

        public Exception? Refusal { get; set; }

        public void Unlock() => Unlocks++;

        public byte[] Agree(byte[] originatorPoint)
        {
            Agreements++;
            if (Refusal is not null)
            {
                throw Refusal;
            }

            using var mine = Certificate.GetECDiffieHellmanPrivateKey()!;
            var size = (originatorPoint.Length - 1) / 2;
            using var theirs = ECDiffieHellman.Create(new ECParameters
            {
                Curve = mine.ExportParameters(false).Curve,
                Q = new ECPoint { X = originatorPoint[1..(1 + size)], Y = originatorPoint[(1 + size)..] },
            });
            return mine.DeriveRawSecretAgreement(theirs.PublicKey);
        }

        public byte[] Decrypt(byte[] encryptedKey, RSAEncryptionPadding padding)
        {
            Decrypts++;
            using var rsa = Certificate.GetRSAPrivateKey()!;
            return rsa.Decrypt(encryptedKey, padding);
        }

        public void Dispose() => Certificate.Dispose();
    }
}
