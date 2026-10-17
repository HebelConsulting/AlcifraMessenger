using SimplArchive.Messenger.Client.Display;
using SimplArchive.Messenger.Client;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// How an opened file leaves the app, as the host already hands on a sealed document: another app shows it, or the share
/// sheet sends it on. The host cleans its copies up as it does its own.
/// </summary>
public interface IFileHandoff
{
    /// <summary>Offers the apps that can show the file.</summary>
    Task OpenWithAsync(byte[] content, string fileName, string contentType);

    /// <summary>Offers the system share sheet.</summary>
    Task ShareAsync(byte[] content, string fileName, string contentType);
}

/// <summary>
/// The Messenger's screens for a host app: what the host supplies — its <see cref="ChatSession"/> (signed-in connection,
/// hardware-key opener), its accent colour and how a file is handed on — is a constructor argument, so leaving one out
/// does not compile.
/// </summary>
public sealed class MessengerScreens(ChatSession session, Color accent, IFileHandoff handoff)
{
    /// <summary>The caller's conversations: where the Messages tile opens.</summary>
    public Page Conversations() => new ConversationsPage(this);

    /// <summary>
    /// Where a "new messages" push opens: the list, then that conversation's thread above it, so Back returns to the
    /// list. Null when <paramref name="conversationId"/> is none of the caller's conversations (another module's signal,
    /// or one they have left since), so the host opens what it would have opened anyway.
    /// </summary>
    public async Task<IReadOnlyList<Page>?> OpenAtAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var list = await session.Api.ConversationsAsync(cancellationToken);
        return list.Items.FirstOrDefault(c => c.Id == conversationId) is { } conversation
            ? [Conversations(), Thread(conversation)]
            : null;
    }

    internal ChatSession Session => session;

    internal Color Accent => accent;

    internal IFileHandoff Handoff => handoff;

    internal Page Thread(Conversation conversation) => new ThreadPage(this, conversation);

    internal Page NewChat(ConversationList list) => new PeoplePage(this, PeopleMode.Chat, list);

    internal Page NewGroup(ConversationList list) => new PeoplePage(this, PeopleMode.Group, list);

    internal Page AddMember(Conversation group) => new PeoplePage(this, PeopleMode.AddMember, group: group);

    internal Page Members(Conversation group) => new MembersPage(this, group);

    /// <summary>Shows a refusal or a failure in words a person can act on; a stale edit says so plainly.</summary>
    internal static Task ShowAsync(Page page, Exception exception) =>
        page.DisplayAlertAsync(ViewText.Get("Failed"), exception switch
        {
            MessengerApiException { Code: "ETAG_MISMATCH" } => ViewText.Get("ChangedElsewhere"),
            MessengerApiException { Code: "MESSENGER_LAST_ADMIN" } => ViewText.Get("LastAdmin"),
            MessengerApiException refusal => refusal.Message,
            _ => exception.Message,
        }, ViewText.Get("OK"));
}
