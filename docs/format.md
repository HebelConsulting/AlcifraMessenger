# The Messenger's format, version 1

The server stores ciphertext and the context below in the clear. It can drop or reorder messages, but it cannot read,
alter or forge one without a member's key.

## Conversation keys

- Each conversation has a **random 256-bit key per epoch**. A new epoch starts whenever a member joins, leaves or is
  removed: a former member cannot read what follows, and a new one cannot read what came before.
- **The next key is never made by the one going.** On a removal, the removing administrator's device makes it; on a
  departure, the epoch is closed and a remaining member's device publishes the next before anything more is sent.
- Each member's copy is a **CMS EnvelopedData** (RFC 5652) addressed to **all** of that member's enrolled certificates,
  so any of their keys opens it:
  - RSA certificates: key transport (PKCS#1 v1.5 or OAEP);
  - elliptic-curve certificates: key agreement (ECDH, the SHA-256 X9.63 KDF, AES-256 key wrap per RFC 3394), with a
    fresh originator key per recipient on that recipient's curve;
  - content encryption AES-256-CBC, the content being the 32-byte key.

## Messages

```
sealed   = 0x01 ‖ nonce (12 bytes) ‖ ciphertext ‖ tag (16 bytes)
cipher   = AES-256-GCM(key of the message's epoch, nonce, plaintext, associated data)
assoc.   = 0x01 ‖ conversation id (16) ‖ epoch (4, big-endian) ‖ message id (16) ‖ sender id (16)
```

- Ids are written as 16 bytes, big-endian (RFC 4122 byte order).
- The **client chooses the message id** before sealing, and the server stores the message under it.
- Because conversation, epoch, id and sender are authenticated, a ciphertext copied anywhere else does not open.
- The nonce is random per message; a key is never used for more than 2^32 messages.
- An edit seals the new text under the current epoch with the same message id. A delete for everyone removes the
  ciphertext and keeps the message's place in the thread.

## Files

A file is a document in the archive: a CMS EnvelopedData of its bytes addressed to every member's certificates at the
time it is sent (the same recipients as above), so it opens with the member's own key outside the Messenger too. A
message carrying a file names the document and seals its file name and type with its text: its plaintext is then the
UTF-8 JSON object `{"text": …, "name": …, "type": …}` instead of the bare text. Which of the two a message holds is
told by the message itself — whether it names an attachment — never guessed from the bytes.

**Several files are one message** (Messenger 0.8.0): the files of a message are named in order, the first as its
attachment as before, and the body adds `"files": [{"name": …, "type": …}, …]` listing every one — the first's `name` and
`type` stay at the top level. A client that knows one file per message therefore reads the first file and the caption,
and a message with one file keeps exactly the shape above (no `files`). Each file is its own envelope, sealed as above.

## Backups

A backup (`MessengerBackup`, core #1813) is **sealed only**: the ciphertext exactly as delivered, the owner's own sealed
copy of each epoch's key, and each file as the envelope it was sent as. Nothing is decrypted on the way out, so it
opens only with a key whose certificate it was sealed to. It is a zip, its entries stored uncompressed:

| entry | holds |
| --- | --- |
| `manifest.json` | `format` (`simplarchive-messenger-backup`), `version` (1), `createdAt`, `instance` (where it was taken), `owner`, and `entries`: every other entry's path with its SHA-256 (lowercase hex) |
| `conversations/<id>/conversation.json` | `id` (the id its messages are bound to), `name`, `isDirect`, `members` (`userId`, `email`, `displayName`), `myUserId` (the owner's id there), `keys` (`epoch`, the owner's sealed `key`) |
| `conversations/<id>/messages.jsonl` | one message per line: `id`, `epoch`, `senderUserId`, `sentAt`, `editedAt`, `deletedAt`, `replyToId`, `ciphertext`, `file`, the blob of its attachment, and `files`, the blobs of any further files in order (absent for none) |
| `blobs/<sha256>` | a file's envelope |

A restore checks every entry against the manifest **before** it changes anything, and keeps every id: a restored
conversation gets a new folder, but its messages still open because the conversation id and the sender ids they are
bound to travel with them. Restoring twice adds nothing; a second member restoring the same conversation joins it.
A backup holds every conversation the user was ever a member of, for the epochs they hold a key for — one they left
included: leaving marks their key (`LeftAt`) rather than deleting it.

Restored messages are marked: the format has no sender signatures (below), so they are as trustworthy as whoever
restored them.

A restored conversation **continues** in a live one, never inside its own epochs: those are bound to the ids they were
sealed under. Continuing makes a new key for the members' certificates here (or joins the one-to-one chat already
started) and places the restored conversation before it as earlier history; each part opens with its own ids.

## Not in version 1

Forward secrecy within an epoch (a ratchet) and signatures by the sender: a member holding the epoch key could forge a
message from another member of the same conversation.
