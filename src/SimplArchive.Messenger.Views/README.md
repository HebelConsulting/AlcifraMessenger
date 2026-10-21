# SimplArchive.Messenger.Views

The SimplArchive Messenger's screens, code-only .NET MAUI: the conversation list, a conversation's thread (bubbles,
replies, edit and delete), the people picker for a new chat or group, and a tablet's split of list and thread.
Everything they do is `SimplArchive.Messenger.Client`'s `ChatSession`; the host supplies the session — its signed-in
connection and its hardware-key opener — its accent colour, how an opened file is handed on, and how a page keeps its
bottom edge clear of the keyboard (applied to every page the screens build, so the compose field stays above it).

Send waits for its answer: from the tap until the server answers, Send and the keyboard's Send key do nothing, the text
stays in the field and the thread shows it as "sending…"; a failed send leaves the text there, never sent twice.

```csharp
var session = new ChatSession(api, keyOpener, myUserId, myEmail);
var screens = new MessengerScreens(session, accent, handoff, keepClearOfKeyboard);
await Navigation.PushAsync(screens.Conversations(split: tabletLayout));
```

With `split: true`, a window at least 700 points wide shows the conversations beside the open conversation, as a
desktop's Messages tab does: a tapped conversation is selected and shown on the right, and New chat, New group and a
group's members open there too, the list staying; a new chat becomes the selected conversation. The divider is fixed.
Narrower, it is the phone's list, and the width is asked again on every size change, so rotating moves the open
conversation between the pane and a page of its own. Whether to offer the split at all is the host's decision.
`OpenAtAsync(conversationId, split)` opens a "new messages" push the same way.

A push that arrives while a screen is open reloads it: the host calls `Arrived(conversationId)` (or `Arrived()` when it
cannot say which conversation) on the same `MessengerScreens` it built the pages from, and the screens on show read
again through the same read their pull-down runs — the list always, a thread only for its own conversation, both panes
of the split. Pushes within half a second are read once; a screen not on show does nothing (it reads itself when it
comes back); while the app is not in front an arrival waits until it is, so no key is asked for unseen; a failed read
is not shown, since nobody asked for it.

```csharp
push.MessagesArrived += (profile, conversation) => screens.Arrived(conversation);
```

Every screen above the list draws the same header, on a phone and in a tablet's pane: the title, its actions as words
and ⋮ for the rest (leaving a group). In the pane it has a ‹ Back of its own; on a phone it is drawn into the page's
navigation bar, whose colours, back arrow and the host's own toolbar entries (a way home) stay the host's. Back on a
phone marks the conversation it came from in the list for a moment, and a chat or group created on a phone is in the
list at once.

Texts in English, German, Italian and Spanish, following the current UI culture. Android now; iOS follows.
Apache License 2.0.
