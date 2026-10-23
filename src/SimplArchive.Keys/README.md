# SimplArchive.Keys

Opens CMS envelopes (S/MIME enveloped data) with the private key on a PIV hardware key, which never leaves it.

- `PivSession`: selects the PIV application, verifies the PIN, reads the key-management certificate (slot 9D), the
  key's version and serial number where it answers them, and performs the one private-key operation, over any
  `IApduTransport`. It also reads the key's **attestation** without the PIN: the slot's attestation certificate (ATTEST)
  and the key's attestation certificate (slot F9), which together prove a key was generated on the token.
- `EnvelopeOpener`: finds the recipient addressed to that certificate and opens the envelope: RSA key transport
  (PKCS#1 v1.5 and OAEP) or elliptic-curve key agreement (ECDH, X9.63 KDF, RFC 3394 key wrap).
- `IKeyConnector`: the one thing an app implements, connecting to the key over NFC or USB on its platform.

Apache License 2.0.
