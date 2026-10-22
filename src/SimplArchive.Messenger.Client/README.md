# SimplArchive.Messenger.Client

The client side of the SimplArchive Messenger, free of any user interface:

- `MessageFormat` seals and opens a message with its conversation's key (AES-256-GCM, with the conversation, epoch,
  message id and sender bound as associated data).
- `ConversationKeys` makes a conversation key and seals it to every certificate of a member (CMS: RSA key transport or
  elliptic-curve key agreement).
- `KeyRing` holds the opened keys of a session, in memory only.
- `IConversationKeyOpener` is what the app supplies: opening its member's sealed copy with the hardware key.
- `MessengerApi` reaches the server by hypermedia: it knows only the archive's root (`/api`) and follows the links the
  server advertises. The app supplies its signed-in `HttpClient`; presigned file transfers use a separate client that
  never carries the archive's token. A refusal arrives as `MessengerApiException` with the server's code
  (`ETAG_MISMATCH`, `MESSENGER_NO_KEY`, …).
- `ChatSession` is a member's session: it opens, sends and continues conversations; `MessengerBackup` writes and
  restores a sealed backup.
- `Display` holds what every app must show alike, free of any user interface: `ThreadRows` (a thread's bubbles —
  sender, quote, the "restored" mark, the receipt line), `MemberRows`, `MessagesSplit` (when a wide window shows the
  conversations beside the open one, and which one the pane keeps after the list is read again), `Arrivals` and
  `ReloadOnArrival` (which open screen a push reloads, and how pushes close together are read once), `SendGate` (one send
  at a time), `KeyPrompt` (a cancelled key prompt is not asked again until the user asks), `TypingSignal` and `TypingIndicator` (when
  the compose field says "is typing" and "stopped", how long a typer is shown, and the line for one, two or more;
  `IMessengerLiveEvents` is the host's live connection they hear it on), and `ViewText`, the
  texts in English, German, Italian and Spanish. The phone's screens and the desktop's tab both draw from it.

The format is specified in the repository's `docs/format.md`. Apache License 2.0.
