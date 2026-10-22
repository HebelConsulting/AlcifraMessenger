# SimplArchive.Messenger.Views

The SimplArchive Messenger's screens, code-only .NET MAUI: the conversation list, a conversation's thread (bubbles,
replies, edit and delete), the people picker for a new chat or group, and a tablet's split of list and thread.
Everything they do is `SimplArchive.Messenger.Client`'s `ChatSession`; the host supplies the session — its signed-in
connection and its hardware-key opener — its accent colour, how an opened file is handed on, its document preview, and
how a page keeps its bottom edge clear of the keyboard (applied to every page the screens build, so the compose field
stays above it), and its live connection (`IMessengerLiveEvents`): the host's own, the one its notifications arrive on,
so the screens open no second one.

"Is typing" (#26): while the compose field is typed in, the thread says so to the other members (again every few
seconds while typing goes on, a stop on send, on leaving the field or after five idle seconds), and shows beneath the
messages who else is typing — "Anna is typing…", "Anna and Tom are typing…", "Several people are typing…" — each
forgotten six seconds after their last event. Both ways or not at all: the switch is under *Messenger settings* in the
conversation list's menu, and off, the server offers no `typing` on the conversation, so the thread neither sends nor
shows it.

Send waits for its answer: from the tap until the server answers, Send and the keyboard's Send key do nothing, the text
stays in the field and the thread shows it as "sending…"; a failed send leaves the text there, never sent twice.

A cancelled key prompt is a decision: the thread shows its messages as sealed ("tap to open it with your key") and asks
again only when the user does — a tap on one, Refresh or a pull — never because it came back to the front (the dialog
closing over it) or because something arrived.

```csharp
var session = new ChatSession(api, keyOpener, myUserId, myEmail);
var screens = new MessengerScreens(session, accent, handoff, preview, keepClearOfKeyboard, liveEvents);
await Navigation.PushAsync(screens.Conversations(split: tabletLayout));
```

An opened file stays in the app wherever the app can show it, because a file handed to another app is a readable copy
the Messenger no longer protects. `FileOpening` (in the client library) decides, from the bytes first, then the sent type,
then the file name:

| the file | where it opens |
| --- | --- |
| a photo (JPEG, PNG, GIF, WebP, BMP, HEIC) | the Messenger's photo page, from memory |
| a PDF or plain text (text, CSV, Markdown) | the host's document preview (`IFilePreview`), from memory |
| a video or a sound both platforms play (MP4, MOV, 3GP; MP3, M4A, AAC, WAV, FLAC) | the Messenger's player, from memory |
| anything else (an office document, say) | a page saying the app cannot show it |

Handing on is never what a tap does: *Open with…* and *Share* sit in the opened file's menu (⋮), and each first warns
that the file leaves the app unsealed and is no longer protected by it. A tenant can keep files in the app altogether
(the module's setting *Keep files in the app*, carried on every conversation as `FilesStayInApp`): the menu then offers
neither, and a file the app cannot show says so and stays closed.

A video or a sound plays in `MemoryPlayer`, a public control a host's own document preview can show too, so a document
and a sent file play alike; `MediaTypes` (in the client library) is the one rule for which types play, and
`FileOpening` asks it. **Sealed content never touches storage** (owner decision, 2026-10-21): what the key opened is
decrypted in memory, and a file written for a player would be a readable copy outside the app's control, left behind by a
crash. So the player reads the bytes where they are:

- on Android, the toolkit's `MediaElement` plays a stream over them;
- on iOS, the system player (`AVPlayer` in an `AVPlayerViewController`) is given an asset at an address of a scheme of
  its own, which it cannot fetch, so it asks a resource loader for everything: the type (the uniform type identifier
  `MediaTypes` names), the length, that any range may be read, and each range — a seek's, and "everything to the end" —
  answered from the array in pieces of at most 1 MiB (`MemoryRanges`), a request the player abandons no longer answered.
  The package therefore carries an iOS build (`net10.0-ios26.0`), which only a Mac with Xcode makes: it is built there
  and packed there, and packing anywhere else refuses rather than ship a package that plays nothing on an iPhone.

Call `Stop()` when the page showing it closes. The host registers the players once on its app builder — the toolkit's
own call stays in the host, chained to `UseMauiApp`, because the toolkit's analyzer looks for it there:

```csharp
builder.UseMauiApp<App>()
    .UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)
    .UseMessengerPlayer();   // iOS: the system player reading from memory
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
