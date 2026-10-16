using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SimplArchive.Keys.Envelopes;
using SimplArchive.Keys.Tests;

namespace SimplArchive.Messenger.Client.Tests;

// The format and the keys, end to end: a key sealed to a member's certificates (RSA and EC) opens on their card through
// the key library, and a message opens only with its own key, in its own conversation, epoch, id and sender.
public sealed class FormatTests
{
    private static readonly MessageContext Context = new(Guid.NewGuid(), 3, Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void A_message_opens_with_its_key_and_context()
    {
        var key = ConversationKeys.NewKey();
        var sealedMessage = MessageFormat.Seal(key, Context, Encoding.UTF8.GetBytes("Hello, Tom"));

        Assert.Equal(MessageFormat.Version, sealedMessage[0]);
        Assert.Equal("Hello, Tom", Encoding.UTF8.GetString(MessageFormat.Open(key, Context, sealedMessage)));
    }

    [Fact]
    public void A_changed_byte_does_not_open()
    {
        var key = ConversationKeys.NewKey();
        var sealedMessage = MessageFormat.Seal(key, Context, Encoding.UTF8.GetBytes("Hello"));
        sealedMessage[^1] ^= 1;

        Assert.Throws<MessageTamperedException>(() => MessageFormat.Open(key, Context, sealedMessage));
    }

    [Theory]
    [InlineData("conversation")]
    [InlineData("epoch")]
    [InlineData("message")]
    [InlineData("sender")]
    public void A_message_moved_to_another_context_does_not_open(string moved)
    {
        var key = ConversationKeys.NewKey();
        var sealedMessage = MessageFormat.Seal(key, Context, Encoding.UTF8.GetBytes("Hello"));
        var elsewhere = moved switch
        {
            "conversation" => Context with { ConversationId = Guid.NewGuid() },
            "epoch" => Context with { Epoch = Context.Epoch + 1 },
            "message" => Context with { MessageId = Guid.NewGuid() },
            _ => Context with { SenderUserId = Guid.NewGuid() },
        };

        Assert.Throws<MessageTamperedException>(() => MessageFormat.Open(key, elsewhere, sealedMessage));
    }

    [Fact]
    public void Another_epochs_key_does_not_open_it()
    {
        var sealedMessage = MessageFormat.Seal(ConversationKeys.NewKey(), Context, [1, 2, 3]);

        Assert.Throws<MessageTamperedException>(() => MessageFormat.Open(ConversationKeys.NewKey(), Context, sealedMessage));
    }

    [Theory]
    [InlineData("rsa")]
    [InlineData("ec")]
    public void A_key_sealed_to_every_certificate_opens_on_each_card(string which)
    {
        // A member with two keys, one of each kind: the envelope addresses both, and either card opens it.
        using var rsa = Rsa();
        using var ec = Ec();
        var key = ConversationKeys.NewKey();
        var wrapped = ConversationKeys.SealTo(key, [rsa.Certificate, ec.Certificate]);

        using var card = which == "rsa" ? rsa.Card() : ec.Card();
        var opened = EnvelopeOpener.Open(wrapped, card, "123456");

        Assert.Equal(key, opened.Content);
    }

    [Fact]
    public async Task The_key_ring_opens_each_key_once_and_forgets_on_clear()
    {
        var opener = new CountingOpener();
        var ring = new KeyRing(opener);
        var conversation = Guid.NewGuid();

        await ring.KeyAsync(conversation, 1, [1]);
        await ring.KeyAsync(conversation, 1, [1]);
        Assert.Equal(1, opener.Opened);

        ring.Clear();
        await ring.KeyAsync(conversation, 1, [1]);
        Assert.Equal(2, opener.Opened);
    }

    private sealed class CountingOpener : IConversationKeyOpener
    {
        public int Opened { get; private set; }

        public Task<byte[]> OpenAsync(byte[] wrappedKey, CancellationToken cancellationToken = default)
        {
            Opened++;
            return Task.FromResult(new byte[32]);
        }
    }

    private sealed class Member(X509Certificate2 certificate, AsymmetricAlgorithm privateKey) : IDisposable
    {
        public X509Certificate2 Certificate { get; } = certificate;

        public FakePivCard Card() => new(Certificate, privateKey);

        public void Dispose()
        {
            Certificate.Dispose();
            privateKey.Dispose();
        }
    }

    private static Member Rsa()
    {
        var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test RSA member", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, critical: false));
        using var withKey = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return new Member(X509CertificateLoader.LoadCertificate(withKey.RawData), key);
    }

    private static Member Ec()
    {
        // Issued by a test CA, as a card's certificate is: a key-agreement-only key cannot sign its own certificate.
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=Test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(2));

        var agreement = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(new X500DistinguishedName("CN=Test EC member"), new PublicKey(agreement), HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyAgreement, critical: false));
        var issued = request.Create(ca.SubjectName, X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), [1, 2, 3, 4]);
        return new Member(issued, agreement);
    }
}
