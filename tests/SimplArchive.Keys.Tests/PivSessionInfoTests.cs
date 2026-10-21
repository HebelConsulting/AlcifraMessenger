using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SimplArchive.Keys.Piv;

namespace SimplArchive.Keys.Tests;

// The key's version and serial, for a support report (core #1864): read without the PIN, and a key that does not
// answer the commands is "not read" rather than a failure.
public sealed class PivSessionInfoTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static FakePivCard Card(byte[]? version, uint? serial)
    {
        var certificate = X509Certificate2.CreateFromPemFile(Path.Combine(Fixtures, "test-rsa.pem"), Path.Combine(Fixtures, "test-rsa.key"));
        return new FakePivCard(X509CertificateLoader.LoadCertificate(certificate.RawData), certificate.GetRSAPrivateKey()!,
            version: version, serial: serial);
    }

    [Fact]
    public void A_key_answering_both_commands_gives_its_version_and_decimal_serial()
    {
        var session = new PivSession(Card([5, 7, 2], 36771502));
        session.Select();

        Assert.Equal("5.7.2", session.ReadVersion());
        Assert.Equal("36771502", session.ReadSerialNumber());
    }

    [Fact]
    public void A_key_without_the_commands_answers_null_rather_than_failing()
    {
        var session = new PivSession(Card(null, null));
        session.Select();

        Assert.Null(session.ReadVersion());
        Assert.Null(session.ReadSerialNumber());
        Assert.NotNull(session.ReadKeyManagementCertificate());   // and the session goes on
    }
}
