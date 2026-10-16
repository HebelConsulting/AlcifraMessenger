# The Messenger's format, version 1

The server stores ciphertext and the context below in the clear. It can drop or reorder messages, but it cannot read,
alter or forge one without a member's key.

## Conversation keys

- Each conversation has a **random 256-bit key per epoch**. A new epoch starts whenever a member leaves or is removed,
  so a former member cannot read what follows. A member who joins receives the current epoch's key, never earlier ones.
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
message carrying a file names the document and seals its file name and type with its text.

## Not in version 1

Forward secrecy within an epoch (a ratchet) and signatures by the sender: a member holding the epoch key could forge a
message from another member of the same conversation.
