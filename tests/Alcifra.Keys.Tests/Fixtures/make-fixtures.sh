#!/usr/bin/env bash
# Throwaway TEST keys and certificates (never used anywhere else), and envelopes addressed to them by an independent
# producer (openssl), so the tests check our opener against a CMS implementation we did not write.
set -euo pipefail
cd "$(dirname "$0")"
openssl req -x509 -newkey rsa:2048 -nodes -keyout test-rsa.key -out test-rsa.pem -days 36500 -subj "/CN=Test RSA reader" 2>/dev/null
openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -keyout test-ec.key -out test-ec.pem -days 36500 -subj "/CN=Test EC reader" 2>/dev/null
printf 'Hello from the test envelope.\n' > content.txt
openssl cms -encrypt -in content.txt -binary -outform DER -aes256 -recip test-rsa.pem -recip test-ec.pem -out both-pkcs1.p7m
openssl cms -encrypt -in content.txt -binary -outform DER -aes256 -recip test-rsa.pem -keyopt rsa_padding_mode:oaep -recip test-ec.pem -out both-oaep.p7m
