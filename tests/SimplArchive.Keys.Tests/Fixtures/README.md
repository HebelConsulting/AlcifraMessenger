# Test fixtures: throwaway keys, test-only

`test-rsa.key` and `test-ec.key` are **throwaway private keys made for these tests and used nowhere else**, with
self-signed "Test … reader" certificates. The envelopes are addressed to both by an independent CMS implementation, so
the tests check this library's opener against code it did not write. Regenerate everything with `make-fixtures.sh`.
