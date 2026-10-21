# SimplArchive Messenger

An end-to-end encrypted messenger for [SimplArchive](https://github.com/HebelConsulting/SimplArchive): conversations
between the people of one SimplArchive installation, sealed to their own hardware keys. The server stores ciphertext
and never sees a message.

Open source under the Apache License 2.0, all of it:

| part | what it is |
| --- | --- |
| **server module** | a SimplArchive industry module: conversations, members, key epochs, messages |
| **client library** | the message format, conversation keys and the API client, free of any user interface |
| **views** | the conversation list, thread and compose screens, for a cross-platform app |
| **app — SAM** | a standalone messenger app built from the library and the views: messages only |
| **key library** | the small hardware-key core: a PIV session and opening an encrypted envelope with the key |

## Status

The server side is in place; the apps are next. The design is settled in the SimplArchive epic and its slices; the code
arrives slice by slice.

- **Key library, `SimplArchive.Keys`**: on nuget.org (0.1.0).
- **Client library, `SimplArchive.Messenger.Client`**: the message format and the conversation keys, specified in
  [docs/format.md](docs/format.md).
- **Server module, `SimplArchive.Messenger`**: conversations, groups with administrators, key epochs, messages, read
  markers, sealed files, and a "new messages" notification that carries nothing of the message. Its manual,
  [manual/SimplArchive-Messenger-Module-Manual.pdf](manual/SimplArchive-Messenger-Module-Manual.pdf), says what the
  server can and cannot see.
- **Screens, `SimplArchive.Messenger.Views`**: the conversation list, the thread (bubbles, replies, edit and delete,
  leaving a group) and the people picker, code-only .NET MAUI for Android, in English, German, Italian and Spanish; on
  a tablet's wide window, the conversations beside the open conversation.
- **Free, no licence:** the module needs no licence file (core ABI 1.23), wherever it runs. From 0.8.0 it needs a core
  with ABI 1.25, whose live hub carries its "is typing" events. `demo/up.sh` is the
  standalone start: one command to a SimplArchive with the Messenger active and test certificates enrolled.
- **On the phone:** the screens run in SimplArchive Mobile's Messages tile — groups with administrators, photos and
  files, read receipts, "Anna is typing…" (on by default, switched off both ways in the Messenger's settings), and a
  "new messages" push that opens its conversation and reloads the open screen. An opened
  file stays in the app wherever the app can show it (photos, PDFs, plain text, video and sound, from memory); anything
  else is handed on only from the menu and after a warning, and a tenant can keep files in the app altogether.
- **Backup and restore:** a user's conversations in one sealed file (`MessengerBackup`), restored on another
  installation as history, which continues as a live conversation once everyone from it is there; the phone backs up
  to a folder daily or weekly. **SAM**, the standalone messages-only app, comes next.

## Building

Everything this repository needs is on nuget.org, so a build needs no account or token. The screens are .NET MAUI for
Android, so a build needs the workload and an Android SDK and JDK (found through `ANDROID_HOME` and `JAVA_HOME`):

```bash
dotnet workload install maui-android
dotnet build SimplArchiveMessenger.slnx
dotnet test SimplArchiveMessenger.slnx
```

## Licence

Apache License 2.0, see [LICENSE](LICENSE). Dependencies must carry a licence compatible with it.
