#!/usr/bin/env bash
set -euo pipefail
# Verifies production profile fail-closed guards: fake providers must be rejected.
# Runs the app image with ASPNETCORE_ENVIRONMENT=Production + fake flags and expects non-zero.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"
COMPOSE_PROJECT_NAME="medical_portal"
set -a; source "$ENV_FILE"; set +a

echo "== Production guard check: fake Stripe must fail fast =="
# Resolve the application image (not postgres/nginx). Prefer the built medical-portal image.
IMG=$(docker images --format '{{.Repository}}:{{.Tag}}' | grep -E 'medical_portal-medical-portal(:latest)?$' | head -1 || true)
if [[ -z "$IMG" ]]; then
  IMG=$(docker compose -p "$COMPOSE_PROJECT_NAME" -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" --profile dev images -q medical-portal 2>/dev/null | head -1 || true)
fi
if [[ -z "$IMG" ]]; then
  echo "No application image found; build first (docker compose --profile dev build)."
  exit 1
fi
echo "Using image: $IMG"
set +e
OUT=$(docker run --rm \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e "ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=medical_portal;Username=portal_app;Password=$PORTAL_APP_PASSWORD" \
  -e "JWT_SECRET=$JWT_SECRET" \
  -e STRIPE_USE_FAKE=true \
  -e HMS_USE_FAKE=true \
  -e "STRIPE_SECRET_KEY=sk_test_placeholder_dev_secret_key_do_not_use_in_prod" \
  "$IMG" 2>&1)
RC=$?
set -e
echo "$OUT" | head -60
if [[ $RC -ne 0 ]] && echo "$OUT" | grep -qi "Fake payment gateway\|Production startup failed\|fake provider\|strictly prohibited"; then
  echo "PASS: production startup rejected fake providers (exit $RC)"
  exit 0
else
  echo "FAIL: expected production to reject fake providers (exit $RC)"
  exit 1
fi
