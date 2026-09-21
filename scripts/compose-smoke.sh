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

echo "Seeded service requests are visible to the manager..."
new_requests="$(curl -fsS "$BASE_URL/api/service-requests?status=New" -H "Authorization: Bearer $token" | jq 'length')"
[ "$new_requests" -ge 3 ] || fail "expected >= 3 New service requests, got $new_requests"
echo "  ok: $new_requests New requests"

# Phase 3 demo walk. It adds two requests per run, so repeated local runs against the same volume
# accumulate a few rows; `docker compose down -v` resets them.
echo "Request intake: staff raise, manager decides, other roles are refused..."
staff_token="$(login staff@sarawakbizops.local)"
tech_token="$(login technician@sarawakbizops.local)"
target="$(curl -fsS "$BASE_URL/api/equipment" -H "Authorization: Bearer $token" | jq -c 'map(select(.status != "Retired"))[0]')"
equipment_id="$(jq -r '.id' <<<"$target")"
customer_id="$(jq -r '.customerId' <<<"$target")"
other_customer_id="$(curl -fsS "$BASE_URL/api/customers" -H "Authorization: Bearer $token" \
  | jq -r --argjson c "$customer_id" 'map(select(.id != $c))[0].id')"

# A POST always carries a body, even an empty one: like a browser's fetch, that sends Content-Length: 0.
# (curl -X POST with no -d sends no length at all, which Kestrel rejects with a bare 400.)
api() { # $1 = token, $2 = method, $3 = path, $4 = optional JSON body -> prints the response body (4xx is not an error here)
  local body=()
  if [ "$2" = POST ]; then body=(-d "${4:-}"); fi
  curl -sS -X "$2" "$BASE_URL/api$3" -H "Authorization: Bearer $1" -H 'Content-Type: application/json' ${body[@]+"${body[@]}"}
}
status_of() { # same arguments as api -> prints only the HTTP status code
  local body=()
  if [ "$2" = POST ]; then body=(-d "${4:-}"); fi
  curl -sS -o /dev/null -w '%{http_code}' -X "$2" "$BASE_URL/api$3" -H "Authorization: Bearer $1" -H 'Content-Type: application/json' ${body[@]+"${body[@]}"}
}
new_body() { jq -nc --argjson c "$1" --argjson e "$equipment_id" --arg p "$2" \
  '{customerId:$c, equipmentId:$e, problemDescription:$p, priority:"High"}'; }

approve_id="$(api "$staff_token" POST /service-requests "$(new_body "$customer_id" 'Smoke test: approve me')" | jq -er '.id')" \
  || fail "staff could not raise a request"
reject_id="$(api "$staff_token" POST /service-requests "$(new_body "$customer_id" 'Smoke test: reject me')" | jq -er '.id')" \
  || fail "staff could not raise a second request"

[ "$(status_of "$staff_token" POST "/service-requests/$approve_id/approve")" = 403 ] \
  || fail "ServiceStaff must not be able to approve"
[ "$(status_of "$tech_token" GET /service-requests)" = 403 ] \
  || fail "Technician must not be able to list service requests"
[ "$(status_of "$staff_token" POST /service-requests "$(new_body "$other_customer_id" 'wrong customer')")" = 400 ] \
  || fail "equipment of another customer must be refused (BR-10)"

api "$token" POST "/service-requests/$approve_id/approve" | jq -e '.status == "Approved"' >/dev/null \
  || fail "manager could not approve"
api "$token" POST "/service-requests/$reject_id/reject" '{"reason":"Smoke test rejection"}' \
  | jq -e '.status == "Rejected" and .rejectionReason == "Smoke test rejection"' >/dev/null \
  || fail "manager could not reject with a reason"
[ "$(status_of "$token" POST "/service-requests/$approve_id/approve")" = 409 ] \
  || fail "approving an already approved request must be a 409"
echo "  ok: raised, approved, rejected, and the wrong roles or invalid moves were refused"

echo "Smoke test passed."
