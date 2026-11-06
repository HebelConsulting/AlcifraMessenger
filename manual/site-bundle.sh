#!/usr/bin/env bash
# Builds the module's SITE BUNDLE (core #2165, core ADR 1010): the feature summaries, screenshots and manual PDF the
# website shows on the module's product page, read out of manual.typ's <site-feature> and <site-figure> markers.
#
#   manual/site-bundle.sh OUT_DIR [VERSION]     # writes OUT_DIR/site-bundle.zip; VERSION defaults to "main"
#
# The bundle's format belongs to the core, so its builder is the core's scripts/site-bundle.py: ONE implementation,
# fetched from the public repository at a pinned commit and checked against its hash, never copied here (as the phone
# app does). Bump both lines together when the core changes the script.
set -euo pipefail
core_commit="9841f0256b0513ef55e1c71e70ceeb15eb0b988d"
core_sha256="8a901785097da46d72558e3d17ad7e41a0a8d7cdcef13cbdef1c74606230f2b6"

out="$(mkdir -p "${1:?usage: manual/site-bundle.sh OUT_DIR [VERSION]}" && cd "$1" && pwd)"
version="${2:-main}"
cd "$(dirname "$0")/.."

script="$out/site-bundle.py"
curl --fail --location --retry 5 --retry-delay 5 --silent --show-error \
  "https://raw.githubusercontent.com/HebelConsulting/Alcifra/$core_commit/scripts/site-bundle.py" -o "$script"
echo "$core_sha256  $script" | shasum -a 256 -c --status || { echo "site-bundle.py does not match its pinned hash" >&2; exit 1; }

rm -rf "$out/site-bundle" "$out/site-bundle.zip"
python3 -I "$script" build --out "$out/site-bundle" --root . --typ manual/manual.typ \
  --pdf "${MANUAL_PDF:-manual/Alcifra-Messenger-Module-Manual.pdf}" \
  --product-id messenger --product-name "Alcifra Messenger" --lang en --version "$version" --commit "$(git rev-parse HEAD)"
python3 -I "$script" validate "$out/site-bundle.zip"
