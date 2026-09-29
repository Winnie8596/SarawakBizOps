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

# A POST/PATCH always carries a body, even an empty one: like a browser's fetch, that sends
# Content-Length: 0. (curl -X POST with no -d sends no length at all, which Kestrel rejects with a bare 400.)
api() { # $1 = token, $2 = method, $3 = path, $4 = optional JSON body -> prints the response body (4xx is not an error here)
  local body=()
  if [ "$2" = POST ] || [ "$2" = PATCH ] || [ -n "${4:-}" ]; then body=(-d "${4:-}"); fi
  curl -sS -X "$2" "$BASE_URL/api$3" -H "Authorization: Bearer $1" -H 'Content-Type: application/json' ${body[@]+"${body[@]}"}
}
status_of() { # same arguments as api -> prints only the HTTP status code
  local body=()
  if [ "$2" = POST ] || [ "$2" = PATCH ] || [ -n "${4:-}" ]; then body=(-d "${4:-}"); fi
  curl -sS -o /dev/null -w '%{http_code}' -X "$2" "$BASE_URL/api$3" -H "Authorization: Bearer $1" -H 'Content-Type: application/json' ${body[@]+"${body[@]}"}
}
# Phase 5 made a RowVersion conflict token mandatory on every workflow mutation. with_rv reads the record's
# current token and merges it into the body, like the web app does from the record it last loaded.
with_rv() { # $1 = token, $2 = record path (e.g. /service-requests/7), $3 = optional JSON object -> body with rowVersion
  local rv
  rv="$(api "$1" GET "$2" | jq -er '.rowVersion')" || fail "could not read the rowVersion of $2"
  jq -nc --arg rv "$rv" --argjson extra "${3:-"{}"}" '$extra + {rowVersion:$rv}'
}
# Phase 8: a customer signature is a multipart upload (the PNG plus the job's rowVersion), not JSON.
signature_png="$(mktemp)"
report_pdf="$(mktemp)" # Phase 10: downloaded reports land here (a file, so a large PDF never meets a closed pipe)
trap 'rm -f "$signature_png" "$report_pdf"' EXIT
# A valid 1x1 PNG, standing in for what the canvas exports.
printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==' \
  | base64 -d > "$signature_png"
sign_status() { # $1 = token, $2 = work order id, $3 = file, $4 = declared type -> prints the HTTP status
  local rv
  rv="$(api "$1" GET "/work-orders/$2" | jq -r '.rowVersion // "AAAAAAAAAAA="')"
  curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE_URL/api/work-orders/$2/signature" \
    -H "Authorization: Bearer $1" -F "file=@-;filename=signature.png;type=$4" -F "rowVersion=$rv" < "$3"
  # The file goes in on stdin: bash opens the path, so a native Windows curl (Git Bash) never has to.
}
# Phase 9: a job photo is the same kind of upload, one photo per request.
photo_status() { # $1 = token, $2 = work order id, $3 = file, $4 = declared type -> prints the HTTP status
  local rv
  rv="$(api "$1" GET "/work-orders/$2" | jq -r '.rowVersion // "AAAAAAAAAAA="')"
  curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE_URL/api/work-orders/$2/photos" \
    -H "Authorization: Bearer $1" -F "file=@-;filename=photo.png;type=$4" -F "rowVersion=$rv" < "$3"
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

api "$token" POST "/service-requests/$approve_id/approve" "$(with_rv "$token" "/service-requests/$approve_id")" | jq -e '.status == "Approved"' >/dev/null \
  || fail "manager could not approve"
api "$token" POST "/service-requests/$reject_id/reject" "$(with_rv "$token" "/service-requests/$reject_id" '{"reason":"Smoke test rejection"}')" \
  | jq -e '.status == "Rejected" and .rejectionReason == "Smoke test rejection"' >/dev/null \
  || fail "manager could not reject with a reason"
[ "$(status_of "$token" POST "/service-requests/$approve_id/approve" "$(with_rv "$token" "/service-requests/$approve_id")")" = 409 ] \
  || fail "approving an already approved request must be a 409"
echo "  ok: raised, approved, rejected, and the wrong roles or invalid moves were refused"

# Phase 4 demo walk: assign -> start -> diagnose -> complete -> approve, plus the wrong roles refused.
echo "First job end to end: assign, technician works it, manager approves..."
pump_request_id="$(curl -fsS "$BASE_URL/api/service-requests?status=Approved" -H "Authorization: Bearer $token" | jq -er '.[0].id')" \
  || fail "expected at least one Approved request to assign (the seeded, still-unassigned pump request)"
technician_id="$(curl -fsS "$BASE_URL/api/technicians" -H "Authorization: Bearer $token" | jq -er '.[0].id')" \
  || fail "expected at least one technician to assign to"

[ "$(status_of "$staff_token" POST "/service-requests/$pump_request_id/assign" "$(jq -nc --arg t "$technician_id" '{technicianId:$t}')")" = 403 ] \
  || fail "ServiceStaff must not be able to assign"

assign_body() { with_rv "$token" "/service-requests/$pump_request_id" "$(jq -nc --arg t "$technician_id" '{technicianId:$t}')"; }
work_order_id="$(api "$token" POST "/service-requests/$pump_request_id/assign" "$(assign_body)" | jq -er '.id')" \
  || fail "manager could not assign the technician"
[ "$(status_of "$token" POST "/service-requests/$pump_request_id/assign" "$(assign_body)")" = 409 ] \
  || fail "a second assign of the same request must be a 409 (BR-12)"

warehouse_token="$(login warehouse@sarawakbizops.local)"
[ "$(status_of "$warehouse_token" POST "/work-orders/$work_order_id/start" '')" = 403 ] \
  || fail "WarehouseStaff must not be able to start a work order"

wo_rv() { with_rv "$tech_token" "/work-orders/$work_order_id" "${1:-}"; }
api "$tech_token" POST "/work-orders/$work_order_id/start" "$(wo_rv)" | jq -e '.status == "InProgress"' >/dev/null \
  || fail "technician could not start the assigned job"
[ "$(status_of "$tech_token" POST "/work-orders/$work_order_id/complete" "$(wo_rv)")" = 400 ] \
  || fail "completing without diagnosis/work performed must be a 400 (AC-05)"

api "$tech_token" PATCH "/work-orders/$work_order_id/diagnosis" \
  "$(wo_rv '{"diagnosis":"Smoke test diagnosis","workPerformed":"Smoke test work performed"}')" \
  | jq -e '.diagnosis == "Smoke test diagnosis"' >/dev/null \
  || fail "technician could not save the diagnosis"

# Phase 8 (BR-03, AC-06): no completion without the customer's signature; only a real PNG is accepted.
[ "$(status_of "$tech_token" POST "/work-orders/$work_order_id/complete" "$(wo_rv)")" = 400 ] \
  || fail "completing without a customer signature must be a 400 (AC-06)"
[ "$(sign_status "$warehouse_token" "$work_order_id" "$signature_png" image/png)" = 403 ] \
  || fail "WarehouseStaff must not be able to upload a signature"
[ "$(sign_status "$tech_token" "$work_order_id" "${BASH_SOURCE[0]}" image/png)" = 400 ] \
  || fail "a file that is not a PNG must be refused, whatever it claims to be"
[ "$(status_of "$token" GET "/work-orders/$work_order_id/signature")" = 404 ] \
  || fail "an unsigned job must have no signature to download"
[ "$(sign_status "$tech_token" "$work_order_id" "$signature_png" image/png)" = 200 ] \
  || fail "technician could not upload the customer's signature"
api "$tech_token" GET "/work-orders/$work_order_id" | jq -e '.hasSignature == true and .signedAt != null' >/dev/null \
  || fail "the job must show that the customer signed"

# Phase 9: photos go up one per request with the job's rowVersion; only JPEG, PNG or WebP bytes are accepted,
# and a photo can be removed until the job is completed. (The 1x1 PNG stands in for a camera photo.)
[ "$(photo_status "$warehouse_token" "$work_order_id" "$signature_png" image/png)" = 403 ] \
  || fail "WarehouseStaff must not be able to upload a photo"
[ "$(photo_status "$tech_token" "$work_order_id" "${BASH_SOURCE[0]}" image/jpeg)" = 400 ] \
  || fail "a file that is not a JPEG, PNG or WebP must be refused, whatever it claims to be"
[ "$(photo_status "$tech_token" "$work_order_id" "$signature_png" image/png)" = 200 ] \
  || fail "technician could not upload a photo"
[ "$(photo_status "$tech_token" "$work_order_id" "$signature_png" image/png)" = 200 ] \
  || fail "technician could not upload a second photo"
removed_photo_id="$(api "$tech_token" GET "/work-orders/$work_order_id" | jq -er '.photos | select(length == 2) | .[0].id')" \
  || fail "the job must list both photos"
api "$tech_token" DELETE "/work-orders/$work_order_id/photos/$removed_photo_id" "$(wo_rv)" | jq -e '(.photos | length) == 1' >/dev/null \
  || fail "technician could not remove a photo before completion"
photo_id="$(api "$tech_token" GET "/work-orders/$work_order_id" | jq -er '.photos[0].id')"

api "$tech_token" POST "/work-orders/$work_order_id/complete" "$(wo_rv)" | jq -e '.status == "Completed"' >/dev/null \
  || fail "technician could not complete the job"

# Phase 10: the Manager generates the completed job's service report (no rowVersion: it documents the job without
# changing it); regenerating keeps the number and bumps the revision until approval locks it.
[ "$(status_of "$tech_token" POST "/work-orders/$work_order_id/generate-report")" = 403 ] \
  || fail "a Technician must not be able to generate a report"
[ "$(status_of "$staff_token" POST "/work-orders/$work_order_id/generate-report")" = 403 ] \
  || fail "ServiceStaff must not be able to generate a report"
[ "$(status_of "$tech_token" GET "/work-orders/$work_order_id/report")" = 404 ] \
  || fail "a job without a report must have nothing to download"
report_number="$(api "$token" POST "/work-orders/$work_order_id/generate-report" | jq -er '.report | select(.revision == 1) | .reportNumber')" \
  || fail "manager could not generate the service report"
[[ "$report_number" =~ ^SR-[0-9]{4}-[0-9]{6,}$ ]] || fail "unexpected report number: $report_number"
api "$token" POST "/work-orders/$work_order_id/generate-report" \
  | jq -e --arg n "$report_number" '.report.reportNumber == $n and .report.revision == 2' >/dev/null \
  || fail "regenerating must keep the report number and bump the revision"

api "$token" POST "/work-orders/$work_order_id/approve" "$(wo_rv)" | jq -e '.status == "Approved"' >/dev/null \
  || fail "manager could not approve the completed job"
[ "$(status_of "$tech_token" POST "/work-orders/$work_order_id/complete" "$(wo_rv)")" = 409 ] \
  || fail "an approved job must be locked against further technician action (AC-07/BR-06)"
# The signature is stored in Azurite and only reachable through the API: the Manager downloads the exact bytes.
curl -fsS "$BASE_URL/api/work-orders/$work_order_id/signature" -H "Authorization: Bearer $token" \
  | cmp -s - "$signature_png" || fail "the Manager must be able to download the stored signature byte for byte"
[ "$(status_of "$warehouse_token" GET "/work-orders/$work_order_id/signature")" = 403 ] \
  || fail "WarehouseStaff must not be able to read a signature"
[ "$(status_of "$tech_token" DELETE "/work-orders/$work_order_id/photos/$photo_id" "$(wo_rv)")" = 409 ] \
  || fail "a photo must not be removable once the job is completed and approved"
curl -fsS "$BASE_URL/api/work-orders/$work_order_id/photos/$photo_id" -H "Authorization: Bearer $token" \
  | cmp -s - "$signature_png" || fail "the Manager must be able to download the stored photo byte for byte"
[ "$(status_of "$warehouse_token" GET "/work-orders/$work_order_id/photos/$photo_id")" = 403 ] \
  || fail "WarehouseStaff must not be able to read a photo"
# Phase 10: approval locked the report (BR-14 per D-05); it stays downloadable by whoever may read the job.
[ "$(status_of "$token" POST "/work-orders/$work_order_id/generate-report")" = 409 ] \
  || fail "an approved job's report must be locked against regeneration (BR-14)"
curl -fsS "$BASE_URL/api/work-orders/$work_order_id/report" -H "Authorization: Bearer $tech_token" -o "$report_pdf" \
  || fail "the technician could not download their job's report"
[ "$(head -c 5 "$report_pdf")" = '%PDF-' ] || fail "the report must download as a PDF"
[ "$(status_of "$warehouse_token" GET "/work-orders/$work_order_id/report")" = 403 ] \
  || fail "WarehouseStaff must not be able to read a report"
api "$staff_token" GET /reports | jq -e --arg n "$report_number" 'map(select(.reportNumber == $n and .isLocked)) | length == 1' >/dev/null \
  || fail "the Reports list must show the job's locked report"
[ "$(status_of "$warehouse_token" GET /reports)" = 403 ] || fail "WarehouseStaff must not be able to list reports"
echo "  ok: assigned, started, diagnosed, photographed, signed, completed, reported and approved; wrong roles and invalid moves were refused"

echo "Seeded completed jobs carry a stored signature and photos..."
seeded_signed="$(api "$token" GET "/work-orders?status=Completed" | jq -er 'map(select(.hasSignature))[0].id')" \
  || fail "the seeded Completed job must be signed (BR-03)"
curl -fsS "$BASE_URL/api/work-orders/$seeded_signed/signature" -H "Authorization: Bearer $token" | head -c 8 | od -An -tx1 \
  | tr -d ' \n' | grep -q '^89504e470d0a1a0a$' || fail "the seeded signature must download as a PNG"
seeded_photo="$(api "$token" GET "/work-orders/$seeded_signed" | jq -er '.photos | select(length == 2) | .[0].id')" \
  || fail "the seeded Completed job must carry two photos"
curl -fsS "$BASE_URL/api/work-orders/$seeded_signed/photos/$seeded_photo" -H "Authorization: Bearer $token" | head -c 8 | od -An -tx1 \
  | tr -d ' \n' | grep -q '^89504e470d0a1a0a$' || fail "the seeded photo must download as a PNG"
echo "  ok: work order #$seeded_signed's signature and photos download as PNGs"

echo "The seeded Approved job carries its locked service report..."
seeded_reported="$(api "$token" GET "/work-orders?status=Approved" | jq -er 'map(select(.report != null and .report.isLocked))[0].id')" \
  || fail "the seeded Approved job must have its (locked) service report"
curl -fsS "$BASE_URL/api/work-orders/$seeded_reported/report" -H "Authorization: Bearer $token" -o "$report_pdf" \
  || fail "the Manager could not download the seeded report"
[ "$(head -c 5 "$report_pdf")" = '%PDF-' ] || fail "the seeded report must download as a PDF"
echo "  ok: work order #$seeded_reported's report downloads as a PDF"

# Phase 6 demo walk: the warehouse runs the parts catalogue and the stock ledger; other roles cannot.
echo "Parts and stock: warehouse adds a part, receives, adjusts; low stock and history; wrong roles refused..."
low_stock="$(api "$warehouse_token" GET /parts/low-stock)"
jq -e 'length >= 4 and any(.[]; .partNumber == "BLT-CNV-600")' <<<"$low_stock" >/dev/null   || fail "expected the seeded low-stock parts (AC-09), got: $low_stock"

[ "$(status_of "$staff_token" GET /parts)" = 403 ] || fail "ServiceStaff must not see inventory"
[ "$(status_of "$staff_token" POST /parts '{"partNumber":"NOPE","name":"Nope","unit":"pcs"}')" = 403 ]   || fail "ServiceStaff must not be able to change inventory"
[ "$(status_of "$tech_token" POST /inventory-transactions '{"partId":1,"transactionType":"Receive","quantity":1}')" = 403 ]   || fail "Technician must not be able to receive stock"

part_number="SMOKE-$(date +%s)"
part_id="$(api "$warehouse_token" POST /parts   "$(jq -nc --arg n "$part_number" '{partNumber:$n, name:"Smoke test part", unit:"pcs", unitCost:12.5, minimumStockLevel:5}')"   | jq -er '.id')" || fail "warehouse could not create a part"
[ "$(status_of "$warehouse_token" POST /parts "$(jq -nc --arg n "$part_number" '{partNumber:$n, name:"Duplicate", unit:"pcs"}')")" = 409 ]   || fail "a duplicate part number must be a 409"

api "$warehouse_token" POST /inventory-transactions "$(jq -nc --argjson p "$part_id" '{partId:$p, transactionType:"Receive", quantity:10, notes:"Smoke delivery"}')"   | jq -e '.quantityOnHandAfter == 10' >/dev/null || fail "warehouse could not receive stock"
[ "$(status_of "$warehouse_token" POST /inventory-transactions "$(jq -nc --argjson p "$part_id" '{partId:$p, transactionType:"Adjustment", quantity:-1}')")" = 400 ]   || fail "an adjustment without a reason must be a 400"
api "$warehouse_token" POST /inventory-transactions "$(jq -nc --argjson p "$part_id" '{partId:$p, transactionType:"Adjustment", quantity:-3, notes:"Smoke stocktake"}')"   | jq -e '.quantityOnHandAfter == 7' >/dev/null || fail "warehouse could not adjust stock with a reason"
[ "$(status_of "$warehouse_token" POST /inventory-transactions "$(jq -nc --argjson p "$part_id" '{partId:$p, transactionType:"Adjustment", quantity:-100, notes:"Too much"}')")" = 409 ]   || fail "stock must never go below zero"
[ "$(status_of "$warehouse_token" POST /inventory-transactions "$(jq -nc --argjson p "$part_id" '{partId:$p, transactionType:"Issue", quantity:-1, notes:"x"}')")" = 400 ]   || fail "Issue transactions must only come from work orders"

history="$(api "$warehouse_token" GET "/inventory-transactions?partId=$part_id")"
jq -e 'length == 2 and (map(.quantity) | add) == 7' <<<"$history" >/dev/null   || fail "the ledger must hold exactly the two movements and sum to the stock on hand, got: $history"
api "$token" GET "/parts/$part_id" | jq -e '.quantityOnHand == 7' >/dev/null || fail "Manager must be able to read the part"
echo "  ok: created, received, adjusted with a reason; the ledger reconciles and the wrong roles were refused"

# Phase 7 demo walk: a technician uses the smoke part (7 on hand) on a fresh job of their own. Like the
# Phase 3 walk it adds a request and a job per run; `docker compose down -v` resets them.
echo "Parts on a job: add, refuse over-stock, return, pause for parts, resume; wrong roles refused..."
stock_is() { api "$warehouse_token" GET "/parts/$part_id" | jq -e --argjson n "$1" '.quantityOnHand == $n' >/dev/null; }
job_request_id="$(api "$staff_token" POST /service-requests "$(new_body "$customer_id" 'Smoke test: parts on a job')" | jq -er '.id')" \
  || fail "staff could not raise the parts-job request"
api "$token" POST "/service-requests/$job_request_id/approve" "$(with_rv "$token" "/service-requests/$job_request_id")" >/dev/null
job_id="$(api "$token" POST "/service-requests/$job_request_id/assign" \
  "$(with_rv "$token" "/service-requests/$job_request_id" "$(jq -nc --arg t "$technician_id" '{technicianId:$t}')")" | jq -er '.id')" \
  || fail "manager could not assign the parts job"
job_rv() { with_rv "$tech_token" "/work-orders/$job_id" "${1:-}"; }
api "$tech_token" POST "/work-orders/$job_id/start" "$(job_rv)" | jq -e '.status == "InProgress"' >/dev/null \
  || fail "technician could not start the parts job"

[ "$(status_of "$tech_token" GET /parts)" = 200 ] || fail "Technician must be able to search the parts catalogue"
[ "$(status_of "$tech_token" GET /inventory-transactions)" = 403 ] || fail "Technician must not read the ledger"
[ "$(status_of "$warehouse_token" POST "/work-orders/$job_id/parts" "$(jq -nc --argjson p "$part_id" '{partId:$p, quantity:1, rowVersion:"AAAAAAAAAAA="}')")" = 403 ] \
  || fail "WarehouseStaff must not add parts to a job"

api "$tech_token" POST "/work-orders/$job_id/parts" "$(job_rv "$(jq -nc --argjson p "$part_id" '{partId:$p, quantity:2}')")" \
  | jq -e '(.parts | length) == 1 and .parts[0].unitPrice == 12.5 and .partsTotal == 25' >/dev/null \
  || fail "technician could not add a part (AC-03)"
stock_is 5 || fail "adding 2 must leave 5 on hand"
[ "$(status_of "$tech_token" POST "/work-orders/$job_id/parts" "$(job_rv "$(jq -nc --argjson p "$part_id" '{partId:$p, quantity:100}')")")" = 409 ] \
  || fail "using more than is in stock must be a 409 (AC-04)"
stock_is 5 || fail "a refused issue must not change the stock"

line_id="$(api "$tech_token" GET "/work-orders/$job_id" | jq -er '.parts[0].id')"
api "$tech_token" DELETE "/work-orders/$job_id/parts/$line_id" "$(job_rv)" | jq -e '(.parts | length) == 0' >/dev/null \
  || fail "technician could not return the part"
stock_is 7 || fail "returning the line must restore the stock"
api "$warehouse_token" GET "/inventory-transactions?workOrderId=$job_id" \
  | jq -e 'length == 2 and (map(.transactionType) | sort) == ["Issue", "Return"]' >/dev/null \
  || fail "the job's ledger must hold exactly one Issue and one Return"

api "$tech_token" POST "/work-orders/$job_id/pending-parts" \
  "$(job_rv "$(jq -nc --argjson p "$part_id" '{reason:"Smoke: waiting for stock", partId:$p, quantity:10}')")" \
  | jq -e '.status == "PendingParts"' >/dev/null || fail "technician could not pause the job for parts"
[ "$(status_of "$tech_token" POST "/work-orders/$job_id/resume" "$(job_rv)")" = 409 ] \
  || fail "resume must be refused while the part is short"
api "$warehouse_token" POST /inventory-transactions "$(jq -nc --argjson p "$part_id" '{partId:$p, transactionType:"Receive", quantity:3, notes:"Smoke top-up"}')" >/dev/null
api "$tech_token" POST "/work-orders/$job_id/resume" "$(job_rv)" | jq -e '.status == "InProgress" and .resumedAt != null' >/dev/null \
  || fail "technician could not resume once the stock arrived"
echo "  ok: added, refused over-stock, returned, paused for parts and resumed; the wrong roles were refused"

echo "Smoke test passed."
