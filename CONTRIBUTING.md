# Contributing

Thank you for helping. A few rules keep this repository easy to build and to trust:

- **It builds from nuget.org alone.** Do not add a package source that needs an account or a token.
- **Licences:** every dependency must carry a licence compatible with Apache-2.0 (MIT, BSD, Apache-2.0 and the like).
- **No warnings:** the build treats warnings as errors; fix the cause rather than suppressing it.
- **Tests come with the change.** Behaviour a change adds or fixes is covered by a test in the same pull request.
- **Never log or commit a secret.** That covers PINs, passwords, tokens and private keys. Test data uses throwaway values.
- **Cryptography stays boring.** Use the platform's audited primitives; a change to the message format comes with its
  specification and with tests for tampering and for the wrong key.

By contributing you agree that your contribution is licensed under the Apache License 2.0.
