using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Client;

/// <summary>What became of a message when the caller tried to read it.</summary>
public enum MessageState
{
    /// <summary>Opened: <see cref="OpenedMessage.Text"/> holds it.</summary>
    Readable,

    /// <summary>The sender deleted it for everyone; its place in the thread stays.</summary>
    Deleted,

    /// <summary>Sealed under an epoch the caller holds no key for: from before they joined.</summary>
    BeforeYouJoined,

    /// <summary>It does not open: altered, or not sealed for this conversation, epoch, id and sender.</summary>
    Unopenable,
}

/// <summary>A message and what reading it gave.</summary>
public sealed record OpenedMessage(Message Message, string? Text, MessageState State)
{
    /// <summary>The file it carries, by name and type, when it carries one and could be read.</summary>
    public SentFile? File { get; init; }
}

/// <summary>A file a message carries: what the message's sealed body says about it. Its bytes are opened on request.</summary>
public sealed record SentFile(string Name, string ContentType);

/// <summary>
/// One person's Messenger session, free of any user interface: sealing and opening text, and every act that makes or
/// hands on a conversation key — starting a chat, creating a group, adding and removing members, leaving, and publishing
/// the next epoch after somebody left. The screens only call this.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys stay in memory</b>, in <see cref="Keys"/>, opened with the caller's hardware key through the app's
/// <see cref="IConversationKeyOpener"/> and forgotten on <see cref="Lock"/>.
/// </para>
/// <para>
/// <b>A new key is sealed to every certificate of every member it is for</b>, read from the server each time, so a member
/// with several cards or devices opens it with any of them.
/// </para>
/// </remarks>
public sealed class ChatSession(MessengerApi api, IConversationKeyOpener opener, Guid me, string myEmail)
{
    /// <summary>The conversation keys opened in this session.</summary>
    public KeyRing Keys { get; } = new(opener);

    /// <summary>The signed-in user.</summary>
    public Guid Me => me;

    /// <summary>The API this session talks to.</summary>
    public MessengerApi Api => api;

    /// <summary>Forgets every opened key: the app locked, the key left, or the app left the screen.</summary>
    public void Lock() => Keys.Clear();

    /// <summary>Every message of <paramref name="conversation"/> the caller can see, oldest first.</summary>
    public async Task<(MessagePage First, IReadOnlyList<Message> Messages)> MessagesAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        var first = await api.MessagesAsync(conversation, cancellationToken);
        var all = new List<Message>(first.Items);
        for (var page = first; page.Items.Count > 0 && await api.NextAsync(page, cancellationToken) is { } next; page = next)
        {
            all.AddRange(next.Items);
        }

        return (first, all);
    }

    /// <summary>Opens <paramref name="message"/> with the key of its own epoch.</summary>
    public async Task<OpenedMessage> OpenAsync(Conversation conversation, Message message, CancellationToken cancellationToken = default)
    {
        if (message.DeletedAt is not null)
        {
            return new OpenedMessage(message, null, MessageState.Deleted);
        }

        if (conversation.MyKeys.FirstOrDefault(k => k.Epoch == message.Epoch) is not { } mine)
        {
            return new OpenedMessage(message, null, MessageState.BeforeYouJoined);
        }

        var key = await Keys.KeyAsync(conversation.Id, message.Epoch, mine.Key, cancellationToken);
        try
        {
            var body = MessageFormat.Open(key, Context(conversation, message.Epoch, message.Id, message.SenderUserId), message.Ciphertext);
            if (message.AttachmentDocumentId is null)
            {
                return new OpenedMessage(message, Encoding.UTF8.GetString(body), MessageState.Readable);
            }

            // A message carrying a file seals its text with the file's name and type (format v1, docs/format.md).
            var withFile = JsonSerializer.Deserialize<FileBody>(body, BodyJson)
                ?? throw new CryptographicException("A message carrying a file has no body.");
            return new OpenedMessage(message, withFile.Text, MessageState.Readable) { File = new SentFile(withFile.Name, withFile.Type) };
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return new OpenedMessage(message, null, MessageState.Unopenable);
        }
    }

    /// <summary>
    /// Sends a file: sealed to EVERY certificate of every member as a CMS envelope (so it opens with a member's own key,
    /// outside the Messenger too), uploaded straight to the store, and named by a message that seals the file's name and
    /// type with <paramref name="text"/>.
    /// </summary>
    public async Task<Message> SendFileAsync(
        Conversation conversation, MessagePage page, byte[] content, string fileName, string contentType, string text = "",
        CancellationToken cancellationToken = default)
    {
        var current = conversation.NeedsNewEpoch ? await PublishNewEpochAsync(conversation, cancellationToken) : conversation;
        var certificates = new List<X509Certificate2>();
        try
        {
            foreach (var member in current.Members)
            {
                foreach (var pem in (await api.CertificatesAsync(member, cancellationToken)).CertificatesPem)
                {
                    certificates.Add(X509Certificate2.CreateFromPem(pem));
                }
            }

            var filed = await api.UploadFileAsync(current, ConversationKeys.SealTo(content, certificates), cancellationToken);
            var id = Guid.NewGuid();
            return await WithCurrentEpochAsync(current, async latest =>
            {
                var key = await Keys.KeyAsync(latest.Id, latest.Epoch, latest.MyKey, cancellationToken);
                var body = JsonSerializer.SerializeToUtf8Bytes(new FileBody(text, fileName, contentType), BodyJson);
                var sealedBody = MessageFormat.Seal(key, Context(latest, latest.Epoch, id, me), body);
                return await api.SendAsync(page, id, latest.Epoch, sealedBody, attachmentDocumentId: filed.DocumentId, cancellationToken: cancellationToken);
            }, cancellationToken);
        }
        finally
        {
            certificates.ForEach(c => c.Dispose());
        }
    }

    /// <summary>The bytes of the file <paramref name="message"/> carries, opened with the member's key (one key use).</summary>
    public async Task<byte[]> OpenFileAsync(Message message, CancellationToken cancellationToken = default) =>
        await opener.OpenAsync(await api.DownloadFileAsync(message, cancellationToken), cancellationToken);

    /// <summary>
    /// Seals and sends <paramref name="text"/>. If somebody left, the next epoch is published first; if the epoch moved on
    /// meanwhile, the conversation is read again and the message sealed once more.
    /// </summary>
    public async Task<Message> SendAsync(
        Conversation conversation, MessagePage page, string text, Guid? replyToId = null, Guid? attachmentDocumentId = null,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        return await WithCurrentEpochAsync(conversation, async current =>
        {
            var key = await Keys.KeyAsync(current.Id, current.Epoch, current.MyKey, cancellationToken);
            var sealedText = MessageFormat.Seal(key, Context(current, current.Epoch, id, me), Encoding.UTF8.GetBytes(text));
            return await api.SendAsync(page, id, current.Epoch, sealedText, replyToId, attachmentDocumentId, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Replaces the text of the caller's own message, sealed under the current epoch with the same id.</summary>
    public Task<Message> EditAsync(Conversation conversation, Message message, string text, CancellationToken cancellationToken = default) =>
        WithCurrentEpochAsync(conversation, async current =>
        {
            var key = await Keys.KeyAsync(current.Id, current.Epoch, current.MyKey, cancellationToken);
            var sealedText = MessageFormat.Seal(key, Context(current, current.Epoch, message.Id, me), Encoding.UTF8.GetBytes(text));
            return await api.EditAsync(message, current.Epoch, sealedText, cancellationToken);
        }, cancellationToken);

    /// <summary>The one conversation with <paramref name="person"/>, created with a new key for both.</summary>
    public async Task<Conversation> StartDirectAsync(ConversationList list, Person person, CancellationToken cancellationToken = default)
    {
        var key = ConversationKeys.NewKey();
        var myself = await MyselfAsync(cancellationToken);
        return await api.OpenDirectAsync(list, person,
            [await WrapAsync(key, me, myself, cancellationToken), await WrapAsync(key, person.Id, person, cancellationToken)], cancellationToken);
    }

    /// <summary>A named group of the caller and <paramref name="people"/>, with a new key for everyone.</summary>
    public async Task<Conversation> CreateGroupAsync(ConversationList list, string name, IReadOnlyList<Person> people, CancellationToken cancellationToken = default)
    {
        var key = ConversationKeys.NewKey();
        var keys = new List<WrappedKey> { await WrapAsync(key, me, await MyselfAsync(cancellationToken), cancellationToken) };
        foreach (var person in people.Where(p => p.Id != me))
        {
            keys.Add(await WrapAsync(key, person.Id, person, cancellationToken));
        }

        return await api.CreateGroupAsync(list, name, keys, cancellationToken);
    }

    /// <summary>
    /// An administrator adds <paramref name="person"/> with a NEW key for everyone, so the newcomer reads from joining on
    /// and nothing sent before.
    /// </summary>
    public async Task AddAsync(Conversation conversation, Person person, CancellationToken cancellationToken = default)
    {
        var key = ConversationKeys.NewKey();
        var keys = new List<WrappedKey>();
        foreach (var member in conversation.Members)
        {
            keys.Add(await WrapAsync(key, member.UserId, member, cancellationToken));
        }

        keys.Add(await WrapAsync(key, person.Id, person, cancellationToken));
        await api.AddMemberAsync(conversation, person.Id, keys, cancellationToken);
    }

    /// <summary>An administrator removes <paramref name="member"/>; their device makes the next key for those who remain.</summary>
    public async Task RemoveAsync(Conversation conversation, Member member, CancellationToken cancellationToken = default)
    {
        var key = ConversationKeys.NewKey();
        var keys = new List<WrappedKey>();
        foreach (var remaining in conversation.Members.Where(m => m.UserId != member.UserId))
        {
            keys.Add(await WrapAsync(key, remaining.UserId, remaining, cancellationToken));
        }

        await api.RemoveMemberAsync(member, keys, cancellationToken);
    }

    /// <summary>The caller leaves; the next key is made by somebody who stays.</summary>
    public Task LeaveAsync(Conversation conversation, CancellationToken cancellationToken = default) =>
        api.LeaveAsync(conversation.Members.Single(m => m.UserId == me), cancellationToken);

    /// <summary>After somebody left: a new key for every member. Another member who did it first makes this a no-op.</summary>
    public async Task<Conversation> PublishNewEpochAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        var key = ConversationKeys.NewKey();
        var keys = new List<WrappedKey>();
        foreach (var member in conversation.Members)
        {
            keys.Add(await WrapAsync(key, member.UserId, member, cancellationToken));
        }

        try
        {
            await api.PublishEpochAsync(conversation, keys, cancellationToken);
        }
        catch (MessengerApiException refusal) when (refusal.Code is "MESSENGER_STALE_EPOCH" or "MESSENGER_EPOCH_OPEN")
        {
            // Somebody else published it first: theirs is the key now.
        }

        return await api.RefreshAsync(conversation, cancellationToken);
    }

    // Writes under the current epoch: publishes the next one first when somebody left, and tries once more when the epoch
    // moved on between reading the conversation and writing.
    private async Task<T> WithCurrentEpochAsync<T>(Conversation conversation, Func<Conversation, Task<T>> write, CancellationToken cancellationToken)
    {
        var current = conversation.NeedsNewEpoch ? await PublishNewEpochAsync(conversation, cancellationToken) : conversation;
        try
        {
            return await write(current);
        }
        catch (MessengerApiException refusal) when (refusal.Code is "MESSENGER_STALE_EPOCH" or "MESSENGER_REKEY_NEEDED")
        {
            current = await api.RefreshAsync(current, cancellationToken);
            if (current.NeedsNewEpoch)
            {
                current = await PublishNewEpochAsync(current, cancellationToken);
            }

            return await write(current);
        }
    }

    // The caller as a person, for their own certificates: found by their e-mail among the people with a key.
    private async Task<Person> MyselfAsync(CancellationToken cancellationToken) =>
        (await api.PeopleAsync(myEmail, cancellationToken)).Items.FirstOrDefault(p => p.Id == me)
        ?? throw new MessengerApiException("MESSENGER_NO_KEY", 409, "You have no enrolled key, so nothing can be sealed to you.");

    private async Task<WrappedKey> WrapAsync(byte[] key, Guid userId, Resource holder, CancellationToken cancellationToken)
    {
        var pems = (await api.CertificatesAsync(holder, cancellationToken)).CertificatesPem;
        if (pems.Count == 0)
        {
            throw new MessengerApiException("MESSENGER_NO_KEY", 409, "A member has no enrolled key.");
        }

        var certificates = pems.Select(pem => X509Certificate2.CreateFromPem(pem)).ToList();
        try
        {
            return new WrappedKey(userId, ConversationKeys.SealTo(key, certificates));
        }
        finally
        {
            certificates.ForEach(c => c.Dispose());
        }
    }

    private static readonly JsonSerializerOptions BodyJson = new(JsonSerializerDefaults.Web);

    /// <summary>A message's body when it carries a file: its text, and the file's name and type.</summary>
    private sealed record FileBody(string Text, string Name, string Type);

    private static MessageContext Context(Conversation conversation, int epoch, Guid messageId, Guid sender) =>
        new(conversation.SealedUnder, epoch, messageId, sender);
}
