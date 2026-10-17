using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using SimplArchive.Messenger.Client;

namespace SimplArchive.Messenger.Tests;

/// <summary>The app's hardware key, stood in for by the private key of the person's certificate.</summary>
internal sealed class PrivateKeyOpener(X509Certificate2 certificate) : IConversationKeyOpener
{
    public Task<byte[]> OpenAsync(byte[] wrappedKey, CancellationToken cancellationToken = default)
    {
        var envelope = new EnvelopedCms();
        envelope.Decode(wrappedKey);
        envelope.Decrypt(new X509Certificate2Collection(certificate));
        return Task.FromResult(envelope.ContentInfo.Content);
    }
}
