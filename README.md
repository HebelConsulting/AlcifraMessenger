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
| **app** | a standalone messenger app built from the library and the views |
| **key library** | the small hardware-key core: a PIV session and opening an encrypted envelope with the key |

## Status

**Just started.** The design is settled in the SimplArchive epic and its slices; the code arrives slice by slice.

- **Key library, `SimplArchive.Keys`** (`src/SimplArchive.Keys`): here, with its tests. Not on nuget.org yet.
- Next: the server module, the client library, the views and the app.

## Building

Everything this repository needs is on nuget.org, so a build needs no account or token:

```bash
dotnet build SimplArchiveMessenger.slnx
dotnet test SimplArchiveMessenger.slnx
```

## Licence

Apache License 2.0, see [LICENSE](LICENSE). Dependencies must carry a licence compatible with it.
