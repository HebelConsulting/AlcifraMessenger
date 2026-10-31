using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Alcifra.Keys.Piv;

namespace Alcifra.Keys.Tests;

// The key's attestation (SimplArchiveMobile #82): what enrolling a key from the phone sends, so the archive can tell a key
// generated on the token from one copied onto it. Read without the PIN; the two refusals are told apart.
public sealed class PivAttestationTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void The_slot_attestation_and_the_attestation_certificate_are_read_without_the_PIN()
    {
        var (slot, device) = Hierarchy();
        var card = Card(slotAttestation: slot.RawData, attestationCertificate: device.RawData);
        var session = new PivSession(card);
        session.Select();

        using var attested = session.Attest();
        using var signer = session.ReadAttestationCertificate();

        Assert.Equal(slot.RawData, attested.RawData);    // longer than one answer: collected over 61xx
        Assert.True(slot.RawData.Length > 256);
        Assert.Equal(device.RawData, signer!.RawData);
        Assert.Equal(0, card.PinAttempts);
    }

    [Fact]
    public void A_firmware_without_attestation_says_so_by_its_own_exception()
    {
        var session = new PivSession(Card(attestRefusal: 0x6D00));
        session.Select();

        Assert.Throws<PivAttestationNotSupportedException>(() => session.Attest());
    }

    [Fact]
    public void An_imported_key_cannot_be_attested_and_the_slot_and_status_are_named()
    {
        var session = new PivSession(Card(attestRefusal: 0x6A80));
        session.Select();

        var refusal = Assert.Throws<PivKeyNotAttestableException>(() => session.Attest());
        Assert.Equal((PivSession.KeyManagementSlot, (ushort)0x6A80), (refusal.Slot, refusal.StatusWord));
    }

    [Fact]
    public void A_key_without_an_attestation_certificate_answers_null()
    {
        var session = new PivSession(Card());
        session.Select();

        Assert.Null(session.ReadAttestationCertificate());
        Assert.NotNull(session.ReadKeyManagementCertificate());   // and the session goes on
    }

    [Fact]
    public void Before_PIV_is_selected_the_key_refuses_as_a_real_one_does()
    {
        var (slot, device) = Hierarchy();
        var session = new PivSession(Card(slotAttestation: slot.RawData, attestationCertificate: device.RawData));

        Assert.Equal(0x6D00, Assert.Throws<PivStatusException>(() => session.ReadAttestationCertificate()).StatusWord);
    }

    private static FakePivCard Card(byte[]? slotAttestation = null, byte[]? attestationCertificate = null, ushort? attestRefusal = null)
    {
        var certificate = X509Certificate2.CreateFromPemFile(Path.Combine(Fixtures, "test-rsa.pem"), Path.Combine(Fixtures, "test-rsa.key"));
        return new FakePivCard(X509CertificateLoader.LoadCertificate(certificate.RawData), certificate.GetRSAPrivateKey()!,
            slotAttestation: slotAttestation, attestationCertificate: attestationCertificate, attestRefusal: attestRefusal);
    }

    // A slot attestation signed by a stand-in for the token's F9 key, as the manufacturer's hierarchy has it.
    private static (X509Certificate2 Slot, X509Certificate2 Device) Hierarchy()
    {
        var now = DateTimeOffset.UtcNow;
        using var deviceKey = RSA.Create(2048);
        var deviceRequest = new CertificateRequest("CN=Test PIV Attestation", deviceKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        deviceRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        var device = deviceRequest.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));

        using var slotKey = RSA.Create(2048);
        var slotRequest = new CertificateRequest("CN=Test PIV Attestation 9d", slotKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var slot = slotRequest.Create(device, now.AddDays(-1), now.AddYears(5), [1, 2, 3, 4]);
        return (X509CertificateLoader.LoadCertificate(slot.RawData), X509CertificateLoader.LoadCertificate(device.RawData));
    }
}
