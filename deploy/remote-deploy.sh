#!/usr/bin/env bash
#
# Runs ON THE VPS, piped in over stdin by .github/workflows/deploy.yml. Can also be run by hand
# when a deploy fails:
#
#   ssh deploy@<host> 'SITE_PATH=/home/deploy/sites/growdesk_marist bash -s' < deploy/remote-deploy.sh
#
set -uo pipefail

SITE_PATH="${SITE_PATH:-}"
GHCR_USERNAME="${GHCR_USERNAME:-}"
GHCR_PAT="${GHCR_PAT:-}"

fail() { echo "::error::$*"; exit 1; }

# Print .env for diagnosis without credentials. CI logs are not a place for secrets.
show_env() {
  sed -E 's/^([A-Za-z_][A-Za-z0-9_]*(PASS|PASSWORD|SECRET|TOKEN|KEY|PAT))=.+/\1=***redacted***/I' .env \
    | grep -v '^#' | grep -v '^$' | sed 's/^/    /'
}

echo "==> host $(hostname), user $(whoami)"

[ -n "$SITE_PATH" ] || fail "VPS_SITE_PATH is empty."
cd "$SITE_PATH" 2>/dev/null || fail "cannot enter $SITE_PATH"
[ -f .env ] || fail "no .env in $SITE_PATH"

missing=""
for key in POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD DB_PORT JWT_KEY API_IMAGE WEB_IMAGE WEB_PORT; do
  grep -q "^${key}=." .env || missing="$missing $key"
done
if [ -n "$missing" ]; then
  echo "Current .env:"; show_env
  fail "missing or empty in .env:$missing"
fi

echo "==> $SITE_PATH"
show_env

# --- registry login (only needed while the images are private) -------------------------------
if [ -n "$GHCR_USERNAME" ] && [ -n "$GHCR_PAT" ]; then
  echo "$GHCR_PAT" | docker login ghcr.io -u "$GHCR_USERNAME" --password-stdin >/dev/null \
    || fail "docker login to ghcr.io failed. Check GHCR_USERNAME and GHCR_PAT (needs read:packages)."
  echo "==> logged in to ghcr.io as $GHCR_USERNAME"
else
  echo "==> no GHCR credentials supplied; assuming the images are public"
fi

# --- pull and restart ---------------------------------------------------------------------
docker compose pull api web || fail "docker compose pull failed"
docker compose up -d --remove-orphans || fail "docker compose up failed"

# --- wait for health ----------------------------------------------------------------------
port=$(grep '^WEB_PORT=' .env | cut -d= -f2-)
echo "==> waiting for http://127.0.0.1:${port}/api/health"
for i in $(seq 1 30); do
  if curl -fsS "http://127.0.0.1:${port}/api/health" >/dev/null 2>&1; then
    echo "==> healthy after $((i * 2))s"
    docker compose ps --format 'table {{.Name}}\t{{.Status}}\t{{.Ports}}'
    docker image prune -f >/dev/null
    exit 0
  fi
  sleep 2
done

echo "==> not healthy after 60s. Recent logs:"
docker compose ps
docker compose logs --tail 60 api web
fail "GrowDesk did not become healthy"
