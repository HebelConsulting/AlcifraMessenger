using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SimplArchive.Messenger.Client.Api;

/// <summary>
/// The Messenger's API, reached by hypermedia: the only address this knows is the archive's root (<c>/api</c>); everything
/// else is a link the server advertised. Every method takes the resource holding the link it follows, so a caller who
/// holds a conversation reaches its messages, members and files without another request.
/// </summary>
/// <remarks>
/// <para>
/// <b>The host signs in.</b> <paramref name="http"/> is the app's own authenticated client, its base address the
/// archive's origin; this library never sees a credential. Presigned file transfers go through
/// <paramref name="storage"/> instead, which carries no token: a presigned address is its own credential, and an
/// archive token has no business reaching the store.
/// </para>
/// <para>Transport only: sealing and opening are <see cref="MessageFormat"/> and <see cref="KeyRing"/>.</para>
/// </remarks>
public sealed class MessengerApi(HttpClient http, HttpClient? storage = null)
{
    /// <summary>The root rel of the caller's conversations.</summary>
    public const string ConversationsRel = "messenger:conversations";

    /// <summary>The root rel of the people search.</summary>
    public const string PeopleRel = "messenger:people";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _storage = storage ?? new HttpClient();
    private Link? _conversations;
    private Link? _people;

    /// <summary>
    /// Reads the archive's root once: false when it offers no Messenger — the module is not active for this tenant, or
    /// this user may not use it — which a client shows as "not available" rather than as an error.
    /// </summary>
    public async Task<bool> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetAsync<RootResource>("/api", cancellationToken);
        (_conversations, _people) = (root.LinkOf(ConversationsRel), root.LinkOf(PeopleRel));
        return _conversations is not null;
    }

    /// <summary>The caller's conversations.</summary>
    public Task<ConversationList> ConversationsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<ConversationList>(Required(_conversations, ConversationsRel).Href, cancellationToken);

    /// <summary>People with an enrolled key whose name or e-mail contains <paramref name="term"/>.</summary>
    public Task<PeopleList> PeopleAsync(string term, CancellationToken cancellationToken = default) =>
        GetAsync<PeopleList>($"{Required(_people, PeopleRel).Href}?term={Uri.EscapeDataString(term)}", cancellationToken);

    /// <summary>The certificates a person's — or a member's — conversation keys are sealed to.</summary>
    public Task<Certificates> CertificatesAsync(Resource personOrMember, CancellationToken cancellationToken = default) =>
        GetAsync<Certificates>(Follow(personOrMember, "certificates").Href, cancellationToken);

    /// <summary>The one conversation with <paramref name="person"/>, created on first use with epoch 1's keys for both.</summary>
    public Task<Conversation> OpenDirectAsync(
        ConversationList list, Person person, IReadOnlyList<WrappedKey> keys, CancellationToken cancellationToken = default) =>
        SendAsync<Conversation>(Follow(list, "open-direct"), new
        {
            counterpartUserId = person.Id,
            counterpartEmail = person.Email,
            keys = Keys(keys),
        }, cancellationToken);

    /// <summary>A named group with the caller as its administrator.</summary>
    public Task<Conversation> CreateGroupAsync(
        ConversationList list, string name, IReadOnlyList<WrappedKey> keys, CancellationToken cancellationToken = default) =>
        SendAsync<Conversation>(Follow(list, "create-group"), new { name, keys = Keys(keys) }, cancellationToken);

    /// <summary>The conversation as it is now.</summary>
    public Task<Conversation> RefreshAsync(Conversation conversation, CancellationToken cancellationToken = default) =>
        GetAsync<Conversation>(Follow(conversation, "self").Href, cancellationToken);

    /// <summary>An administrator adds <paramref name="userId"/> by starting a new epoch, with its key for every member and the newcomer.</summary>
    public Task AddMemberAsync(Conversation conversation, Guid userId, IReadOnlyList<WrappedKey> keys, CancellationToken cancellationToken = default) =>
        SendAsync(Follow(conversation, "add-member"), new { userId, keys = Keys(keys) }, cancellationToken);

    /// <summary>An administrator removes a member, with the next epoch's keys for those who remain.</summary>
    public Task RemoveMemberAsync(Member member, IReadOnlyList<WrappedKey> nextKeys, CancellationToken cancellationToken = default) =>
        SendAsync(Follow(member, "remove"), new { nextKeys = Keys(nextKeys) }, cancellationToken);

    /// <summary>
    /// The caller leaves, with no keys: the next epoch is published by a member who remains, never by the one leaving,
    /// who would then know it. <paramref name="myself"/> is the caller's own entry among the members.
    /// </summary>
    public Task LeaveAsync(Member myself, CancellationToken cancellationToken = default) =>
        SendAsync(Follow(myself, "remove"), new { nextKeys = Array.Empty<object>() }, cancellationToken);

    /// <summary>After somebody left: a new key for every member, published before anything more is sent.</summary>
    public Task PublishEpochAsync(Conversation conversation, IReadOnlyList<WrappedKey> keys, CancellationToken cancellationToken = default) =>
        SendAsync(Follow(conversation, "publish-epoch"), new { keys = Keys(keys) }, cancellationToken);

    /// <summary>An administrator makes a member an administrator.</summary>
    public Task MakeAdminAsync(Member member, CancellationToken cancellationToken = default) =>
        SendAsync(Follow(member, "make-admin"), body: null, cancellationToken);

    /// <summary>An administrator takes that back.</summary>
    public Task RevokeAdminAsync(Member member, CancellationToken cancellationToken = default) =>
        SendAsync(Follow(member, "revoke-admin"), body: null, cancellationToken);

    /// <summary>Moves the caller's read marker forward; it also clears their "new messages" notification.</summary>
    public Task MarkReadAsync(Conversation conversation, DateTimeOffset readUpTo, CancellationToken cancellationToken = default) =>
        SendAsync(Follow(conversation, "receipt"), new { readUpTo }, cancellationToken);

    /// <summary>
    /// The caller started (<paramref name="typing"/>) or stopped typing in <paramref name="conversation"/> (#26). Offered
    /// only where the caller's typing indicator is on; the server tells the other members' open apps, and answers alike
    /// whether it did.
    /// </summary>
    public Task TypingAsync(Conversation conversation, bool typing, CancellationToken cancellationToken = default) =>
        SendAsync(Follow(conversation, "typing"), new { typing }, cancellationToken);

    /// <summary>The caller's own preferences (#26), from the conversation list's <c>preferences</c> link.</summary>
    public Task<MessengerPreferences> PreferencesAsync(ConversationList list, CancellationToken cancellationToken = default) =>
        GetAsync<MessengerPreferences>(Follow(list, "preferences").Href, cancellationToken);

    /// <summary>
    /// Replaces the caller's preferences, provided nobody changed them since <paramref name="read"/> (another device): a
    /// refusal is <c>ETAG_MISMATCH</c>. Returns them as stored, with their new tag.
    /// </summary>
    public Task<MessengerPreferences> ReplacePreferencesAsync(MessengerPreferences read, bool typingIndicator, CancellationToken cancellationToken = default) =>
        SendAsync<MessengerPreferences>(new Link("replace", Follow(read, "self").Href, "PUT"), new { typingIndicator }, cancellationToken, read.ETag);

    /// <summary>The first page of messages, oldest first.</summary>
    public Task<MessagePage> MessagesAsync(Conversation conversation, CancellationToken cancellationToken = default) =>
        GetAsync<MessagePage>(Follow(conversation, "messages").Href, cancellationToken);

    /// <summary>The page after <paramref name="page"/>, or null when the server offers none.</summary>
    public async Task<MessagePage?> NextAsync(MessagePage page, CancellationToken cancellationToken = default) =>
        page.LinkOf("next") is { } next ? await GetAsync<MessagePage>(next.Href, cancellationToken) : null;

    /// <summary>Posts a message sealed under <paramref name="epoch"/> with the id bound into it.</summary>
    public Task<Message> SendAsync(
        MessagePage page, Guid messageId, int epoch, byte[] ciphertext, Guid? replyToId = null, Guid? attachmentDocumentId = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<Message>(Follow(page, "send"), new
        {
            id = messageId,
            epoch,
            ciphertext,
            replyToId,
            attachmentDocumentId,
        }, cancellationToken);

    /// <summary>Replaces the text of the caller's own message, provided nobody changed it since <paramref name="message"/> was read.</summary>
    public Task<Message> EditAsync(Message message, int epoch, byte[] ciphertext, CancellationToken cancellationToken = default) =>
        SendAsync<Message>(Follow(message, "edit"), new { epoch, ciphertext }, cancellationToken, message.ETag);

    /// <summary>Deletes the caller's own message for everyone, its file with it.</summary>
    public Task DeleteForEveryoneAsync(Message message, CancellationToken cancellationToken = default) =>
        SendAsync(Follow(message, "delete"), body: null, cancellationToken, message.ETag);

    /// <summary>Hides a message for the caller only.</summary>
    public Task HideForMeAsync(Message message, CancellationToken cancellationToken = default) =>
        SendAsync(Follow(message, "hide"), body: null, cancellationToken);

    /// <summary>Files a sealed file in the conversation: begin, PUT to the presigned address, complete.</summary>
    public async Task<FiledFile> UploadFileAsync(Conversation conversation, byte[] sealedFile, CancellationToken cancellationToken = default)
    {
        var upload = await SendAsync<Upload>(Follow(conversation, "upload-file"), body: null, cancellationToken);
        if (sealedFile.LongLength > upload.MaxBytes)
        {
            throw new MessengerApiException("MESSENGER_FILE_TOO_LARGE", 413, $"The file is {sealedFile.LongLength} bytes; at most {upload.MaxBytes} are accepted.");
        }

        using (var put = new HttpRequestMessage(HttpMethod.Put, upload.Url) { Content = new ByteArrayContent(sealedFile) })
        {
            put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pkcs7-mime");
            using var response = await _storage.SendAsync(put, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new MessengerApiException("MESSENGER_UPLOAD_FAILED", (int)response.StatusCode, "The store refused the file.");
            }
        }

        return await SendAsync<FiledFile>(Follow(upload, "complete"), body: null, cancellationToken);
    }

    /// <summary>The sealed file a message carries. The server answers with a redirect to the store, followed without the token.</summary>
    public async Task<byte[]> DownloadFileAsync(Message message, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Follow(message, "attachment").Href);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect
            && response.Headers.Location is { } location)
        {
            // A host that does not follow redirects itself: the store gets no token, only its own presigned address.
            return await _storage.GetByteArrayAsync(location, cancellationToken);
        }

        await ThrowIfFailedAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Restores one conversation of a backup (core #1813) as read-only history, or joins the one another member restored:
    /// <paramref name="myKeys"/> are the caller's own sealed keys per epoch, exactly as the backup holds them.
    /// </summary>
    public Task<Conversation> RestoreAsync(
        ConversationList list, Guid originConversationId, string originInstance, string name, IReadOnlyList<BackupMember> members,
        Guid myUserId, IReadOnlyDictionary<int, byte[]> myKeys, CancellationToken cancellationToken = default) =>
        RestoreAsync(list, originConversationId, originInstance, name, false, members, myUserId, myKeys, cancellationToken);

    /// <summary>
    /// As <see cref="RestoreAsync(ConversationList, Guid, string, string, IReadOnlyList{BackupMember}, Guid, IReadOnlyDictionary{int, byte[]}, CancellationToken)"/>,
    /// saying whether it was a one-to-one chat, which continues as the chat with that person. An overload, not an optional
    /// parameter: a caller compiled against the earlier signature keeps binding to it.
    /// </summary>
    public Task<Conversation> RestoreAsync(
        ConversationList list, Guid originConversationId, string originInstance, string name, bool isDirect, IReadOnlyList<BackupMember> members,
        Guid myUserId, IReadOnlyDictionary<int, byte[]> myKeys, CancellationToken cancellationToken = default) =>
        SendAsync<Conversation>(Follow(list, "restore"), new
        {
            originConversationId,
            originInstance,
            name,
            isDirect,
            members = members.Select(m => new { userId = m.UserId, email = m.Email, displayName = m.DisplayName }),
            myUserId,
            keys = myKeys.Select(k => new { epoch = k.Key, key = k.Value }),
        }, cancellationToken);

    /// <summary>Imports a batch of a restored conversation's messages, ids kept; those already there are skipped.</summary>
    public Task<ImportResult> ImportAsync(Conversation restored, IReadOnlyList<BackupMessage> messages, CancellationToken cancellationToken = default) =>
        SendAsync<ImportResult>(Follow(restored, "import"), new { messages }, cancellationToken);

    /// <summary>Who a restored conversation's members are here: whether it can continue, and whose certificates its key needs.</summary>
    public Task<Continuation> ContinuationAsync(Conversation restored, CancellationToken cancellationToken = default) =>
        GetAsync<Continuation>(Follow(restored, "continuation").Href, cancellationToken);

    /// <summary>
    /// Continues restored history as a live conversation, in one request: <paramref name="keys"/> is a new key for every
    /// member (empty when a one-to-one chat joins the one already here). Returns the live conversation.
    /// </summary>
    public Task<Conversation> ContinueAsync(Continuation continuation, IReadOnlyList<WrappedKey> keys, CancellationToken cancellationToken = default) =>
        SendAsync<Conversation>(new Link("continue", Follow(continuation, "self").Href, "POST"), new { keys = Keys(keys) }, cancellationToken);

    /// <summary>The restored history shown above a live conversation, as far as the caller holds its keys.</summary>
    public Task<ConversationList> EarlierAsync(Conversation live, CancellationToken cancellationToken = default) =>
        GetAsync<ConversationList>(Follow(live, "earlier").Href, cancellationToken);

    private static List<object> Keys(IReadOnlyList<WrappedKey> keys) => [.. keys.Select(k => new { userId = k.UserId, key = k.Key })];

    private static Link Follow(Resource resource, string rel) =>
        resource.LinkOf(rel) ?? throw new MessengerApiException("MESSENGER_NOT_OFFERED", 404, $"The server offers no '{rel}' here.");

    private static Link Required(Link? link, string rel) =>
        link ?? throw new InvalidOperationException($"Call DiscoverAsync first; the root offered no '{rel}'.");

    private async Task<T> GetAsync<T>(string href, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(href, cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    private async Task<T> SendAsync<T>(Link link, object? body, CancellationToken cancellationToken, string? ifMatch = null)
    {
        using var response = await SendRawAsync(link, body, ifMatch, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    private async Task SendAsync(Link link, object? body, CancellationToken cancellationToken, string? ifMatch = null)
    {
        using var response = await SendRawAsync(link, body, ifMatch, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(Link link, object? body, string? ifMatch, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod(link.Method), link.Href)
        {
            Content = body is null ? null : JsonContent.Create(body, options: Json),
        };
        if (ifMatch is { Length: > 0 })
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        var response = await http.SendAsync(request, cancellationToken);
        try
        {
            await ThrowIfFailedAsync(response, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    // RFC 7807: the server's code and detail, so a client can answer a 412 ("somebody else changed this") differently
    // from a 409 ("this person has no key").
    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ProblemDetails? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            // Not a problem document: the status says what is known.
        }

        throw new MessengerApiException(
            problem?.ErrorCode ?? $"HTTP_{(int)response.StatusCode}", (int)response.StatusCode,
            problem?.Detail ?? problem?.Title ?? response.ReasonPhrase ?? "The request failed.");
    }

    private sealed class RootResource : Resource;

    private sealed class ProblemDetails
    {
        public string? Title { get; set; }

        public string? Detail { get; set; }

        /// <summary>The core writes the code as the <c>errorCode</c> extension.</summary>
        public string? ErrorCode { get; set; }
    }
}

/// <summary>The server refused: its problem code (<c>MESSENGER_NO_KEY</c>, <c>ETAG_MISMATCH</c>…), status and message.</summary>
public sealed class MessengerApiException(string code, int statusCode, string message) : Exception(message)
{
    /// <summary>The server's problem code.</summary>
    public string Code { get; } = code;

    /// <summary>The HTTP status.</summary>
    public int StatusCode { get; } = statusCode;
}
