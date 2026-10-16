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

The format is specified in the repository's `docs/format.md`. Apache License 2.0.
