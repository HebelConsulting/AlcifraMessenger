using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Alcifra.Messenger.Client;

/// <summary>Who wrote a message, where and when in the key's life: bound into its ciphertext as associated data.</summary>
/// <param name="ConversationId">The conversation (its folder's id).</param>
/// <param name="Epoch">The key epoch the message was sealed under.</param>
/// <param name="MessageId">The id the client chose before sealing; the server stores the message under it.</param>
/// <param name="SenderUserId">The member who sent it.</param>
public sealed record MessageContext(Guid ConversationId, int Epoch, Guid MessageId, Guid SenderUserId);

/// <summary>A message that does not open with the key it claims: altered, moved, or sealed under another key.</summary>
public sealed class MessageTamperedException(string message) : CryptographicException(message);

/// <summary>
/// The Messenger's message format, version 1 (see <c>docs/format.md</c>):
/// <c>0x01 ‖ nonce (12) ‖ ciphertext ‖ tag (16)</c>, AES-256-GCM under the conversation key of the message's epoch.
/// </summary>
/// <remarks>
/// <b>The context is the associated data, so a message cannot be moved.</b> Copying a ciphertext into another conversation,
/// another epoch, under another id or another sender fails to open, because each of those is authenticated with it. The
/// server stores ciphertext and the context in the clear; it can drop or reorder messages, but not alter or forge them
/// without a member's key.
/// </remarks>
public static class MessageFormat
{
    /// <summary>The format version this client writes and reads.</summary>
    public const byte Version = 1;
    private const int NonceSize = 12, TagSize = 16, KeySize = 32;

    /// <summary>Seals <paramref name="plaintext"/> under <paramref name="key"/> for <paramref name="context"/>.</summary>
    public static byte[] Seal(ReadOnlySpan<byte> key, MessageContext context, ReadOnlySpan<byte> plaintext)
    {
        RequireKey(key);
        var sealedMessage = new byte[1 + NonceSize + plaintext.Length + TagSize];
        sealedMessage[0] = Version;
        var nonce = sealedMessage.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, sealedMessage.AsSpan(1 + NonceSize, plaintext.Length),
            sealedMessage.AsSpan(1 + NonceSize + plaintext.Length, TagSize), AssociatedData(context));
        return sealedMessage;
    }

    /// <summary>Opens a sealed message, or throws <see cref="MessageTamperedException"/>.</summary>
    public static byte[] Open(ReadOnlySpan<byte> key, MessageContext context, ReadOnlySpan<byte> sealedMessage)
    {
        RequireKey(key);
        if (sealedMessage.Length < 1 + NonceSize + TagSize)
        {
            throw new MessageTamperedException("This is too short to be a sealed message.");
        }

        if (sealedMessage[0] != Version)
        {
            throw new MessageTamperedException($"Message format version {sealedMessage[0]} is not one this client reads.");
        }

        var length = sealedMessage.Length - 1 - NonceSize - TagSize;
        var plaintext = new byte[length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(sealedMessage.Slice(1, NonceSize), sealedMessage.Slice(1 + NonceSize, length),
                sealedMessage.Slice(1 + NonceSize + length, TagSize), plaintext, AssociatedData(context));
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new MessageTamperedException("This message does not open: it was altered, moved, or sealed under another key.");
        }
    }

    // version ‖ conversation (16) ‖ epoch (4, big-endian) ‖ message (16) ‖ sender (16): fixed width, so no two contexts
    // encode alike.
    private static byte[] AssociatedData(MessageContext context)
    {
        var data = new byte[1 + 16 + 4 + 16 + 16];
        data[0] = Version;
        context.ConversationId.TryWriteBytes(data.AsSpan(1, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(17, 4), context.Epoch);
        context.MessageId.TryWriteBytes(data.AsSpan(21, 16), bigEndian: true, out _);
        context.SenderUserId.TryWriteBytes(data.AsSpan(37, 16), bigEndian: true, out _);
        return data;
    }

    private static void RequireKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException("A conversation key is 32 bytes.", nameof(key));
        }
    }
}
