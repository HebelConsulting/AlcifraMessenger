#!/usr/bin/env bash
# Stops the Messenger demo. Pass -v to drop its data as well (its own volumes only: the project is messenger-demo).
set -euo pipefail
cd "$(dirname "$0")"
docker compose down "$@"
