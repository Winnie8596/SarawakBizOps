#!/usr/bin/env bash
# Smoke test for the Docker setup. Assumes `docker compose up -d --wait` has already succeeded.
# Everything goes through the WEB container (nginx -> API), exactly as a browser would.
#
#   BASE_URL       default http://localhost:8080
#   DEMO_PASSWORD  default Demo!2026 (must match docker-compose.yml / .env)
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
DEMO_PASSWORD="${DEMO_PASSWORD:-Demo!2026}"

fail() { echo "FAIL: $*" >&2; exit 1; }

login() { # $1 = email -> prints the JWT
  curl -fsS -X POST "$BASE_URL/api/auth/login" \
    -H 'Content-Type: application/json' \
    -d "$(jq -n --arg e "$1" --arg p "$DEMO_PASSWORD" '{email:$e, password:$p}')" | jq -er '.token'
}

echo "Web app serves the SPA shell..."
curl -fsS "$BASE_URL/" | grep -q '<div id="root"' || fail "index.html has no #root"

echo "SPA deep links fall back to index.html..."
curl -fsS "$BASE_URL/customers" | grep -q '<div id="root"' || fail "deep link did not return the app shell"

echo "API readiness through the proxy..."
curl -sS "$BASE_URL/api/customers" -o /dev/null -w '%{http_code}\n' | grep -q 401 \
  || fail "unauthenticated /api/customers should be 401"

echo "Each demo role can sign in and gets the right role..."
for pair in admin:Admin manager:Manager staff:ServiceStaff technician:Technician warehouse:WarehouseStaff; do
  user="${pair%%:*}"; role="${pair##*:}"
  token="$(login "$user@sarawakbizops.local")" || fail "login failed for $user"
  curl -fsS "$BASE_URL/api/auth/me" -H "Authorization: Bearer $token" | jq -e --arg r "$role" '.roles | index($r)' >/dev/null \
    || fail "$user does not have role $role"
  echo "  ok: $user ($role)"
done

echo "Seeded data is visible to the manager..."
token="$(login manager@sarawakbizops.local)"
customers="$(curl -fsS "$BASE_URL/api/customers" -H "Authorization: Bearer $token" | jq 'length')"
equipment="$(curl -fsS "$BASE_URL/api/equipment" -H "Authorization: Bearer $token" | jq 'length')"
[ "$customers" -ge 5 ] || fail "expected >= 5 customers, got $customers"
[ "$equipment" -ge 10 ] || fail "expected >= 10 equipment, got $equipment"
echo "  ok: $customers customers, $equipment equipment"

echo "Smoke test passed."
