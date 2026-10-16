# SimplArchive.Messenger.Views

The SimplArchive Messenger's screens, code-only .NET MAUI: the conversation list, a conversation's thread (bubbles,
replies, edit and delete), and the people picker for a new chat or group. Everything they do is
`SimplArchive.Messenger.Client`'s `ChatSession`; the host supplies the session — its signed-in connection and its
hardware-key opener — and its accent colour.

```csharp
var session = new ChatSession(api, keyOpener, myUserId, myEmail);
await Navigation.PushAsync(new MessengerScreens(session, accent).Conversations());
```

Texts in English, German, Italian and Spanish, following the current UI culture. Android now; iOS follows.
Apache License 2.0.
