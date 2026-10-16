using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SimplArchive.Keys.Envelopes;
using SimplArchive.Keys.Piv;

namespace SimplArchive.Keys.Tests;

// Opening openssl-made envelopes with a key that is only reachable through PIV APDUs: the same path the phone takes
// with a real hardware key, minus the radio. One envelope reaches two keys (RSA and P-256), as one person's several
// certificates do, and each card opens only its own recipient.
public sealed class EnvelopeOpenerTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly byte[] Content = File.ReadAllBytes(Path.Combine(Fixtures, "content.txt"));

    private static FakePivCard Card(string name)
    {
        var certificate = X509Certificate2.CreateFromPemFile(Path.Combine(Fixtures, $"{name}.pem"), Path.Combine(Fixtures, $"{name}.key"));
        AsymmetricAlgorithm key = certificate.GetRSAPrivateKey() as AsymmetricAlgorithm ?? certificate.GetECDiffieHellmanPrivateKey()!;
        return new FakePivCard(X509CertificateLoader.LoadCertificate(certificate.RawData), key);
    }

    private static byte[] Envelope(string name) => File.ReadAllBytes(Path.Combine(Fixtures, name));

    [Theory]
    [InlineData("both-pkcs1.p7m")]
    [InlineData("both-oaep.p7m")]
    public void An_rsa_key_opens_its_key_transport_recipient(string envelope)
    {
        var opened = EnvelopeOpener.Open(Envelope(envelope), Card("test-rsa"), "123456");

        Assert.Equal(Content, opened.Content);
        Assert.StartsWith("RSA key transport", opened.Scheme, StringComparison.Ordinal);
    }

    [Fact]
    public void A_p256_key_opens_its_key_agreement_recipient()
    {
        var opened = EnvelopeOpener.Open(Envelope("both-pkcs1.p7m"), Card("test-ec"), "123456");

        Assert.Equal(Content, opened.Content);
        Assert.Equal("EC key agreement (P-256)", opened.Scheme);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Content)), opened.Sha256);
    }

    [Fact]
    public void A_wrong_pin_is_named_with_the_attempts_left_and_nothing_is_decrypted()
    {
        var refused = Assert.Throws<PivWrongPinException>(() => EnvelopeOpener.Open(Envelope("both-pkcs1.p7m"), Card("test-rsa"), "654321"));

        Assert.Equal(2, refused.RetriesLeft);
    }

    [Fact]
    public void An_envelope_not_addressed_to_this_key_is_refused_before_the_pin_is_spent()
    {
        // Addressed to the EC key only; the RSA card must refuse without asking for the PIN (a wrong guess costs a try).
        var card = Card("test-rsa");
        var other = new System.Security.Cryptography.Pkcs.EnvelopedCms(new System.Security.Cryptography.Pkcs.ContentInfo(Content));
        using var rsaOnlyElsewhere = RSA.Create(2048);
        var stranger = new CertificateRequest("CN=Stranger", rsaOnlyElsewhere, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        other.Encrypt(new System.Security.Cryptography.Pkcs.CmsRecipient(stranger));

        Assert.Throws<EnvelopeException>(() => EnvelopeOpener.Open(other.Encode(), card, "123456"));
        Assert.Equal(0, card.PinAttempts);
    }

    [Fact]
    public void A_file_that_is_not_an_envelope_is_named()
    {
        Assert.Throws<EnvelopeException>(() => EnvelopeOpener.Open("not an envelope"u8.ToArray(), Card("test-rsa"), "123456"));
    }
}
