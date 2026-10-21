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
/// The host's document preview (#27), the viewer its own documents open in: a PDF's pages or a plain text, with find and
/// pinch zoom, drawn from memory — nothing of the file is written to storage.
/// </summary>
public interface IFilePreview
{
    /// <summary>
    /// A page showing <paramref name="content"/> (a PDF or a plain text, as <paramref name="contentType"/> says), which lets
    /// go of what it drew once it is closed. The screens push it, and add Open with… and Share to its menu where files may
    /// leave the app; its own toolbar entries (find) stay the host's.
    /// </summary>
    ContentPage Open(byte[] content, string fileName, string contentType);
}

/// <summary>
/// The Messenger's screens for a host app: what the host supplies — its <see cref="ChatSession"/> (signed-in connection,
/// hardware-key opener), its accent colour, how a file is handed on, its document preview and how a page keeps clear of
/// the keyboard — is a constructor argument, so leaving one out does not compile.
/// </summary>
/// <param name="preview">Where a PDF or a plain text opens: the host's own preview, in the app (#27).</param>
/// <param name="keepClearOfKeyboard">Applied to every page the screens build, so the compose field stays above the
/// keyboard: the host's own way, as for its other pages (on Android 15+, drawn edge to edge, the system no longer
/// resizes the window for it; on iOS a host may pad the page in front itself and pass a no-op).</param>
public sealed class MessengerScreens(
    ChatSession session, Color accent, IFileHandoff handoff, IFilePreview preview, Action<ContentPage> keepClearOfKeyboard)
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

    /// <summary>
    /// Something new arrived for the caller — a "new messages" push, in <paramref name="conversationId"/> where the host
    /// knows which (#26). The screens built here that are on show read again, through the same read their pull-down
    /// runs: the list of conversations always, a thread only when it is that conversation's or the host cannot say.
    /// Arrivals close together are read once; a screen that is not on show does nothing (it reads itself when it comes
    /// back), and while the app is not in front an arrival waits until it is, so no hardware key is asked for behind the
    /// user's back. A failed read is not shown: nobody asked for it. Safe to call from any thread.
    /// </summary>
    /// <remarks>Only the screens of THIS instance are reached: a host keeps one <see cref="MessengerScreens"/> per
    /// signed-in session and builds every page from it.</remarks>
    public void Arrived(Guid? conversationId = null) => Open.Arrived(conversationId);

    /// <summary>The screens on show, which <see cref="Arrived"/> reloads.</summary>
    internal OpenScreens Open { get; } = new();

    internal ChatSession Session => session;

    internal Color Accent => accent;

    internal IFileHandoff Handoff => handoff;

    internal IFilePreview Preview => preview;

    /// <summary>Keeps <paramref name="page"/>'s bottom edge clear of the keyboard, as the host does it.</summary>
    internal void KeepClearOfKeyboard(ContentPage page) => keepClearOfKeyboard(page);

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
