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
    /// <summary>The caller's conversations: where the Messages tile opens, as on a phone.</summary>
    public Page Conversations() => Conversations(split: false);

    /// <summary>
    /// The caller's conversations: where the Messages tile opens. With <paramref name="split"/>, a window at least
    /// <see cref="MessagesSplit.MinimumWidth"/> points wide shows the open conversation beside the list, as the desktop's
    /// Messages tab does — New chat, New group and a group's members open there too, the list staying — and a narrower
    /// one is the phone's list; the width is asked again on every size change. Whether to offer the split at all (a
    /// tablet layout, a subscription) is the host's to decide.
    /// </summary>
    public Page Conversations(bool split) => new ConversationsPage(this, split);

    /// <summary>
    /// Where a "new messages" push opens, as on a phone: the list, then that conversation's thread above it, so Back
    /// returns to the list. Null when <paramref name="conversationId"/> is none of the caller's conversations (another
    /// module's signal, or one they have left since), so the host opens what it would have opened anyway.
    /// </summary>
    public Task<IReadOnlyList<Page>?> OpenAtAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        OpenAtAsync(conversationId, split: false, cancellationToken);

    /// <summary>
    /// Where a "new messages" push opens. With <paramref name="split"/> it is one page, <see cref="Conversations(bool)"/>
    /// with that conversation selected — beside the list in a wide window, pushed as its page once a narrow one is laid
    /// out; without, the list and the thread above it. Null when <paramref name="conversationId"/> is none of the
    /// caller's conversations.
    /// </summary>
    public async Task<IReadOnlyList<Page>?> OpenAtAsync(Guid conversationId, bool split, CancellationToken cancellationToken = default)
    {
        var list = await session.Api.ConversationsAsync(cancellationToken);
        if (list.Items.FirstOrDefault(c => c.Id == conversationId) is not { } conversation)
        {
            return null;
        }

        if (split)
        {
            return [new ConversationsPage(this, split: true, conversation)];
        }

        var conversations = new ConversationsPage(this, split: false);
        return [conversations, new ScreenPage(this, Thread(conversation), conversations)];
    }

    internal ChatSession Session => session;

    internal Color Accent => accent;

    internal IFileHandoff Handoff => handoff;

    // The screens, each built for the host it is given: a page of its own, or the split's pane.
    internal Func<IScreenHost, ScreenView> Thread(Conversation conversation) => host => new ThreadView(this, host, conversation);

    internal Func<IScreenHost, ScreenView> People(PeopleMode mode, ConversationList list) => host => new PeopleView(this, host, mode, list);

    internal Func<IScreenHost, ScreenView> AddMember(Conversation group) => host => new PeopleView(this, host, PeopleMode.AddMember, group: group);

    internal Func<IScreenHost, ScreenView> Members(Conversation group) => host => new MembersView(this, host, group);

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
