# SimplArchive.Messenger.Views

The SimplArchive Messenger's screens, code-only .NET MAUI: the conversation list, a conversation's thread (bubbles,
replies, edit and delete), the people picker for a new chat or group, and a tablet's split of list and thread.
Everything they do is `SimplArchive.Messenger.Client`'s `ChatSession`; the host supplies the session — its signed-in
connection and its hardware-key opener — its accent colour, and how an opened file is handed on.

```csharp
var session = new ChatSession(api, keyOpener, myUserId, myEmail);
var screens = new MessengerScreens(session, accent, handoff);
await Navigation.PushAsync(screens.Conversations(split: tabletLayout));
```

With `split: true`, a window at least 700 points wide shows the conversations beside the open conversation, as a
desktop's Messages tab does: a tapped conversation is selected and shown on the right, and New chat, New group and a
group's members open there too, the list staying; a new chat becomes the selected conversation. The divider is fixed.
Narrower, it is the phone's list, and the width is asked again on every size change, so rotating moves the open
conversation between the pane and a page of its own. Whether to offer the split at all is the host's decision.
`OpenAtAsync(conversationId, split)` opens a "new messages" push the same way.

Every screen above the list draws the same header, on a phone and in a tablet's pane: the title, its actions as words
and ⋮ for the rest (leaving a group). In the pane it has a ‹ Back of its own; on a phone it is drawn into the page's
navigation bar, whose colours, back arrow and the host's own toolbar entries (a way home) stay the host's. Back on a
phone marks the conversation it came from in the list for a moment, and a chat or group created on a phone is in the
list at once.

Texts in English, German, Italian and Spanish, following the current UI culture. Android now; iOS follows.
Apache License 2.0.
