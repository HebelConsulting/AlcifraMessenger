#!/usr/bin/env bash
# One command to an Alcifra with the Messenger ACTIVE on its Demo tenant — the standalone start of this repository:
# builds the module, brings up the core's development stack with it (the public core repository checked out
# beside this one, or wherever ALCIFRA_CORE points), then — through saconsole as the seeded setup account —
# activates the module (it needs no licence, core ADR 0944), grants it its repository, configures it, and enrols a TEST
# certificate for three demo users. Nothing is left for whoever runs it to switch on.
#
#   demo/up.sh            the stack, the Messenger active, certificates for demo@, anna@ and tom@
#   demo/down.sh [-v]     stop it (-v drops its data)
#
# Walked on an emulator (2026-10-17): the Messages tile, the list, the people picker and a new chat all work against
# this stack; sending stops at the key dialog, which only a hardware key answers.
#
# Needs: Docker, the .NET SDK, python3 and openssl. The test certificates' private keys stay in demo/.local/,
# git-ignored; they stand in for hardware keys, so they open nothing a real key would.
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(cd .. && pwd)"
CORE="$(cd "${ALCIFRA_CORE:-${SIMPLARCHIVE_CORE:-../../Alcifra}}" 2>/dev/null && pwd || true)"
[ -n "$CORE" ] || { echo "ERROR: no core checkout at ${ALCIFRA_CORE:-../../Alcifra} (clone https://github.com/HebelConsulting/Alcifra beside this repository, or set ALCIFRA_CORE)"; exit 1; }
export ALCIFRA_CORE="$CORE"   # docker-compose.yaml includes the core's stack from the same place
HOST=http://localhost:8080

echo "==> Building the Messenger module…"
dotnet build "$ROOT/src/Alcifra.Messenger" -c Release -v quiet
mkdir -p modules/messenger .local
cp "$ROOT/src/Alcifra.Messenger/bin/Release/net10.0/Alcifra.Messenger.dll" modules/messenger/

echo "==> Bringing up the core stack with the module (the first run builds the Api image)…"
docker compose up -d --build
echo -n "==> Waiting for the Api"
until curl -fsS "$HOST/health/ready" >/dev/null 2>&1; do echo -n "."; sleep 3; done
echo " ready."

# saconsole from the core checkout: the certificate commands are newer than a released tool may be.
dotnet build "$CORE/src/Alcifra.Cli" -c Release -v quiet
sac() { dotnet "$CORE/src/Alcifra.Cli/bin/Release/net10.0/saconsole.dll" "$@"; }

# The Demo tenant's stable ids (core #781): the same derivation the core's seeders use.
demo_id() {
  python3 - "$1" <<'PY'
import sys, uuid
tenant = uuid.uuid5(uuid.UUID('5A21B5E0-DE20-4E11-9D5B-8F1C2E40D0D5'), 'tenant/Demo')
print(tenant if sys.argv[1] == 'tenant' else uuid.uuid5(tenant, sys.argv[1]))
PY
}
REPOSITORY="$(demo_id repository/flight-school)"   # the seeded setup account's repository: conversations go here

echo "==> Signing in as the setup account…"
# The secret rides in the login's own environment; the token is taken by an exact-shape match, never eval'd.
login_out="$(SACONSOLE_CLIENT_SECRET=dev-messenger-setup-secret sac login --url "$HOST" --client-id messenger-setup)"
SACONSOLE_TOKEN="$(printf '%s\n' "$login_out" | sed -n "s/^export SACONSOLE_TOKEN='\([A-Za-z0-9._~+\/=-]*\)'\$/\1/p")"
[ -n "$SACONSOLE_TOKEN" ] || { echo "ERROR: saconsole login printed no token"; exit 1; }
export SACONSOLE_TOKEN SACONSOLE_URL="$HOST"

echo "==> Activating the Messenger, granting it its repository, configuring it…"
sac module activate --module messenger   # no licence: the Messenger needs none (core ADR 0944)
sac acl grant "$REPOSITORY" --service-account "Module: Messenger" \
  --rights see,read-content,edit-content,edit-index-data,create-sub-items,delete,manage-permissions
printf '%s' "$REPOSITORY" | sac module settings set messenger messenger.repositoryId --stdin

echo "==> Enrolling a test certificate for demo@, anna@ and tom@…"
# Reader certificates are filed in a "Reader certificates" folder the core creates under a parent the tenant chooses
# (core ADR 0936); here, the repository the setup account administers.
sac certificates settings set --parent "$REPOSITORY"
for person in demo anna tom; do
  email="$person@simplarchive.local"
  if [ ! -f ".local/$person.pem" ]; then
    openssl req -x509 -newkey rsa:2048 -nodes -days 365 -subj "/CN=$person test key/emailAddress=$email" \
      -addext "keyUsage=critical,keyEncipherment,digitalSignature" -addext "subjectAltName=email:$email" \
      -keyout ".local/$person.key" -out ".local/$person.pem" 2>/dev/null
  fi
  sac certificates enrol ".local/$person.pem" --holder "$email" --label "Test key" || echo "   ($email: enrolment refused, see above)"
done

cat <<DONE

==> Ready.
    App:      $HOST  (demo@simplarchive.local / SimplDemo2026!; anna@ and tom@ likewise)
    Emulator: adb reverse tcp:8080 tcp:8080, then server http://localhost:8080 in a DEBUG build of the phone app
              (debug builds alone permit plain HTTP, to localhost and 10.0.2.2); the sign-in page must reach the same
              address the stack's issuer names.
    The test keys in demo/.local/ stand in for hardware keys; an emulator cannot open a message with them.

    Stop with demo/down.sh
DONE
