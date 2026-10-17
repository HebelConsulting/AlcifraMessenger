using SimplArchive.Messenger.Client;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// The Messenger's screens for a host app: what the host supplies — its <see cref="ChatSession"/> (signed-in connection,
/// hardware-key opener) and its accent colour — is a constructor argument, so leaving one out does not compile.
/// </summary>
public sealed class MessengerScreens(ChatSession session, Color accent)
{
    /// <summary>The caller's conversations: where the Messages tile opens.</summary>
    public Page Conversations() => new ConversationsPage(this);

    internal ChatSession Session => session;

    internal Color Accent => accent;

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
