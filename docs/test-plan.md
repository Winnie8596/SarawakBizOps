# Test plan and acceptance-criteria map

Every acceptance criterion (AC-01 … AC-11, from the System Design Document) must end up with at least one named,
passing automated test. The map below grows a phase at a time (see [`plan.md`](../plan.md)); Phase 15 only verifies it
and fills gaps. A blank "Test" cell means the criterion is not implemented yet.

## How the tests are organised

| Layer | Where | What it proves |
|---|---|---|
| Unit | `tests/SarawakBizOps.Api.Tests` (`*StateMachineTests`, `*RulesTests`, `ServiceReportBuilderTests`, `ServiceReportPdfTests`) | Pure rules with no database: every lifecycle transition, allowed and forbidden; upload and report rules; the report's data model and the PDF writer |
| Integration | `tests/SarawakBizOps.Api.Tests` | The real API against a real SQL Server container (Testcontainers): business rules, validation, concurrency, seed data |
| Authorization | `EndpointAccessTests` | Every endpoint × (anonymous + all five roles) → exactly 401, 403 or the success status (AC-11) |
| Frontend | `src/SarawakBizOps.Web/src/test` (Vitest + React Testing Library) | The UI honours role gating and shows the API's messages; `fetch` is stubbed, the components are real |
| Storage contract | `FileStorageContractTests` | One set of `IFileStorage` expectations, run against the in-memory fake (unit) and `AzureBlobFileStorage` on an Azurite container (integration), so the fake can stand in for real storage |
| Docker smoke | `scripts/compose-smoke.sh` (CI job `compose-smoke`) | The shipped containers work together: sign-in through nginx, seed data, request intake, the first job (with photos, the signature and the service report stored in Azurite), parts and stock, parts on a job |
| Ledger reconciliation | `Infrastructure/LedgerReconciliation` | Straight from the database: `QuantityOnHand == SUM(ledger)` for **every** part. Every stock-moving test in `InventoryTests` and `WorkOrderPartsTests` ends with it, and `DemoSeedingTests` runs it on the seed |

Conventions: each integration test creates its own uniquely named customers/users, so tests never depend on run order;
the `Api` collection shares one SQL Server container and one Azurite container per test run.

## Acceptance-criteria map

| AC | Statement (short) | Phase | Test(s) | Status |
|---|---|---|---|---|
| AC-01 | Assign creates exactly one WorkOrder; request marked assigned | 4 | `WorkOrderTests.Assigning_an_approved_request_creates_one_work_order_and_marks_it_assigned`, `Assigning_the_same_request_twice_fails_on_the_second_attempt`, `Two_managers_racing_to_assign_the_same_request_have_exactly_one_winner` | **Done** |
| AC-02 | Technician sees customer, equipment, problem, priority, status | 4 | `WorkOrderTests.The_assigned_technician_sees_full_job_context_and_it_appears_in_their_job_list`, `Another_technician_cannot_read_or_act_on_someone_elses_job` | **Done** |
| AC-03 | Add part → one WorkOrderPart + one Issue transaction, atomically | 7 | `WorkOrderPartsTests.Adding_a_part_creates_one_line_and_exactly_one_issue_row_and_lowers_stock`, `A_failure_part_way_through_adding_a_part_leaves_nothing_behind` (failure injected before and after the rows are written: stock, lines, ledger and the job's `rowVersion` all unchanged), `The_price_is_snapshotted_from_the_catalogue_and_a_client_price_is_ignored`; UI: `TechnicianView.test.tsx` › `adds a part picked from the catalogue…` | **Done** |
| AC-04 | Insufficient stock rejected, stock unchanged | 7 | `WorkOrderPartsTests.Adding_more_than_is_in_stock_is_refused_and_changes_nothing`, `Technicians_racing_for_the_last_units_never_take_more_than_there_is` (the concurrent-issue test, five rounds); UI: `shows the server’s not-enough-stock refusal…`, `pauses the job for a part that is out of stock…` | **Done** |
| AC-05 | Completion rejected without diagnosis / work performed | 4 | `WorkOrderTests.Completing_without_diagnosis_or_work_performed_is_rejected`, `Saving_a_diagnosis_with_a_blank_field_is_rejected_and_the_job_still_cannot_complete` | **Done** |
| AC-06 | Completion rejected without a signature | 8 | `WorkOrderSignatureTests.Completing_without_a_signature_is_rejected_and_succeeds_once_the_customer_has_signed`, `The_diagnosis_is_still_asked_for_first_when_both_are_missing`; `WorkOrderTests.Completing_with_both_fields_and_the_signature_recorded_succeeds`; UI: `Signature.test.tsx` › `refuses to complete until the customer has signed…`; smoke: complete-without-signature is a 400 | **Done** |
| AC-07 | Manager approval locks the work order | 4, 5 | `WorkOrderTests.Approving_a_completed_job_locks_it_against_further_technician_action` (status changes); `WorkflowCancellationTests.A_technician_cannot_edit_the_diagnosis_of_an_approved_job` (the central lock guard also covers non-transition edits) | **Done** |
| AC-08 | Technician edit after approval rejected | 5 | `WorkflowCancellationTests.A_technician_cannot_edit_the_diagnosis_of_an_approved_job` | **Done** |
| AC-09 | Low-stock parts appear on the dashboard | 6, 11 | API: `InventoryTests.Low_stock_lists_active_parts_at_or_below_their_minimum_and_nothing_else`, `Receiving_enough_stock_takes_a_part_off_the_low_stock_list`, `DemoSeedingTests.Demo_parts_reconcile_with_their_ledger_and_four_active_parts_are_low_on_stock`; UI (Inventory page): `Inventory.test.tsx` › `shows parts with RM prices and highlights the ones low on stock`, `switches to the low-stock endpoint…`; dashboard (Phase 11): `AnalyticsTests.Low_stock_summary_counts_active_parts_at_or_below_minimum_and_agrees_with_the_low_stock_list`, `A_part_that_falls_low_appears_on_the_Manager_dashboard`, `DemoSeedingTests.Dashboard_numbers_equal_the_hand_computed_values_of_the_seed`; UI: `Dashboard.test.tsx` › `shows the server-computed tiles and charts to a Manager` | **Done** (API Phase 6, dashboard Phase 11) |
| AC-10 | Manager AI uses backend-computed data only | 14 | | Not yet implemented |
| AC-11 | Unauthorized role rejected regardless of the UI | every phase | `EndpointAccessTests.Each_endpoint_allows_only_its_roles` (rows: the six `/api/service-requests` + eleven work-order/technician endpoints, including both Phase 5 `/cancel` routes, + the seven Phase 6 parts/inventory endpoints, `GET /parts` now also allowing a Technician, + the four Phase 7 parts/pending-parts/resume endpoints, + the two Phase 8 signature upload/download endpoints, + the three Phase 9 photo upload/remove/download endpoints, + the three Phase 10 report endpoints: generate, download, list, + the Phase 11 `GET /dashboard/summary`); `EquipmentAndHistoryTests` (equipment update, history), `UserManagementTests` (users) for the Phase 1 endpoints | **In progress**: rows are added with each phase; Phase 15 verifies the whole surface |

## Business rules covered so far

| Rule | Test(s) |
|---|---|
| BR-08 server-side authorization | `EndpointAccessTests`; frontend `RoleRouting.test.tsx` shows the UI matches (route guards and sidebar per role) |
| BR-10 equipment must belong to the customer | `ServiceRequestTests.Equipment_that_belongs_to_another_customer_is_rejected_and_nothing_is_saved`; UI: `Raising a request › offers only the chosen customer’s serviceable equipment` |
| BR-01 (partial) a request is reviewed before it can go further | `ServiceRequestStateMachineTests` (only `New→Approved` and `New→Rejected` exist); the assignment half lands in Phase 4 |
| Reject requires a reason | `ServiceRequestTests.Rejecting_without_a_reason_is_a_400_and_leaves_the_request_New`; UI: `requires a reason before a rejection can be confirmed` |
| Invalid transitions are a clear 409 | `ServiceRequestTests.Approving_twice_is_a_409_that_says_why`, `A_decided_request_cannot_be_decided_the_other_way`; UI: `shows the conflict message and refreshes…` |
| Two simultaneous decisions have one winner | `ServiceRequestTests.Concurrent_decisions_on_one_request_have_exactly_one_winner` (6 racing calls: 1 × 200, 5 × 409; run repeatedly before merge) |
| Creator comes from the token | `ServiceRequestTests.The_creator_comes_from_the_token_not_from_the_request_body` |
| Timestamps stored in UTC, shown in MYT | UI: `shows dates in Malaysia time (UTC+8)…` |
| Demo data is consistent and idempotent | `DemoSeedingTests` (12 requests and 6 work orders in the right states, including one Cancelled of each and a PendingParts job, parts used on three jobs, BR-10 holds for every seeded row, re-seeding changes nothing) |
| BR-01 (assignment half), BR-13 request follows the job | `WorkOrderTests` full-workflow test; `ServiceRequestStateMachineTests`/`WorkOrderTests` cover the two halves separately since assignment moves the *request*, not the work order |
| BR-02 a technician acts only on their own job | `WorkOrderTests.Another_technician_cannot_read_or_act_on_someone_elses_job` (get, start and `mine` all checked); ownership is reported as 404, not 403, so a technician cannot even confirm another's job exists |
| BR-11 only an active Technician can be assigned | `WorkOrderTests.Assigning_to_a_non_technician_or_inactive_technician_is_rejected` |
| BR-12 one *live* WorkOrder per request | `WorkOrderTests.Assigning_the_same_request_twice_fails_on_the_second_attempt`, `Two_managers_racing_to_assign_the_same_request_have_exactly_one_winner`; `WorkflowCancellationTests.A_manager_cancels_an_assigned_job_and_the_request_becomes_reassignable` (the filtered unique index on `WorkOrder.ServiceRequestId`, excluding `Cancelled` rows, is the final guard both ways) |
| Work-order lifecycle guards | `WorkOrderStateMachineTests` (all 49 (from, to) pairs, including the Cancel targets and InProgress ⇄ PendingParts; start only from Assigned, resume only from PendingParts); `WorkOrderTests.Starting_a_job_twice_is_a_409`, `Approving_a_job_that_is_not_completed_is_a_409` |
| BR-06/AC-08 central lock guard (Phase 5) | `WorkflowCancellationTests.A_technician_cannot_edit_the_diagnosis_of_an_approved_job` (a locked job refuses a non-transition edit, not just a status change, via `WorkOrderStateMachine.IsLocked`) |
| Cancellation (Phase 5) | `WorkflowCancellationTests` — request cancel by creator-side roles (New only) and by a Manager (New or Approved); work-order cancel by a Manager (New/Assigned/InProgress) with the request-revert side effect; both require a reason |
| BR-09 immutable ledger, stock == SUM(ledger) (Phase 6) | `InventoryTests.Receiving_stock_writes_exactly_one_ledger_row_and_raises_the_stock_in_step`, `Editing_a_part_changes_the_catalogue_fields_but_never_the_stock`, `No_route_can_edit_or_delete_a_ledger_row` (enumerates the app's routes: only `GET`/`POST` on the collection), plus `LedgerReconciliation` after every stock-moving test |
| Adjustment requires a reason; Issue/Return only from work orders (Phase 6) | `StockMovementRulesTests`; `InventoryTests.An_adjustment_without_a_reason_is_rejected_and_changes_nothing`, `Issue_and_return_cannot_be_created_through_the_inventory_endpoint`; UI: `will not submit an adjustment until a reason is given…` |
| BR-04 groundwork: stock never below zero (Phase 6) | `InventoryTests.An_adjustment_that_would_take_stock_below_zero_is_a_409_and_changes_nothing`, `Concurrent_write_offs_of_the_last_units_never_take_stock_below_zero` (8 racing write-offs for 3 units: exactly 3 win), `Concurrent_receives_all_land_and_the_total_is_exact`; a `CHECK (QuantityOnHand >= 0)` constraint backs it in the database |
| BR-04 stock never below zero when used on a job; concurrency (Phase 7) | `WorkOrderPartsTests.Technicians_racing_for_the_last_units_never_take_more_than_there_is` — per round, N technicians each with their own job race for the last units of one part (8 for 3, 6 for 1, 8 for 2.5 in 0.5s, repeated): exactly `stock / quantity` succeed, the rest are 409, stock ends at exactly 0, one Issue per winner, one line per winning job, and the whole database reconciles. Also `A_double_tap_on_the_same_job_view_adds_the_part_once` (the job's `rowVersion` claim) |
| BR-05 each part used writes exactly one Issue linked to the job; returns write one Return (Phase 7) | `WorkOrderPartsTests.Adding_a_part_creates_one_line_and_exactly_one_issue_row_and_lowers_stock`, `Returning_a_line_puts_its_stock_back_with_one_return_row` (and a second return is a 404), `A_line_can_only_be_returned_through_its_own_job`, `The_office_sees_the_parts_and_the_ledger_can_be_filtered_by_job` |
| Parts only on your own job, only while InProgress (Phase 7) | `WorkOrderPartsTests.Another_technician_cannot_see_or_change_parts_on_my_job` (404), `Parts_can_only_change_while_the_job_is_in_progress` (Assigned/Completed 409, Approved → the central lock message), `A_stale_row_version_is_a_409_and_a_missing_one_is_a_400`, `An_invalid_quantity_is_a_400`, `Unknown_and_inactive_parts_cannot_be_used`, `A_technician_can_search_only_the_active_catalogue` |
| PendingParts and the resume stock guard (Phase 7) | `WorkOrderPartsTests.A_job_waits_for_parts_and_resumes_only_once_they_are_in_stock` (no parts or Complete while paused, resume refused while short, allowed once received, `StartedAt` kept), `Waiting_for_a_part_that_is_already_in_stock_is_refused`, `Pending_parts_needs_a_reason_and_a_job_in_progress`, `Start_cannot_be_used_to_skip_the_resume_stock_guard`, `A_manager_can_cancel_a_job_that_is_waiting_for_parts`, `A_job_waiting_for_parts_still_counts_as_open_work_for_its_technician` |
| RowVersion conflict token (Phase 5) | `WorkflowCancellationTests.Two_diagnosis_edits_against_the_same_stale_RowVersion_have_exactly_one_winner` (closes the lost-update gap a pure status guard cannot: two edits to the same field, same status), `Approving_a_request_with_a_stale_RowVersion_is_a_409`, `A_missing_or_malformed_RowVersion_is_a_400` |

## Phase 3 test inventory

**Backend (xUnit)**
- `ServiceRequestStateMachineTests`: all 25 (from, to) pairs against an independently written expectation, plus the refusal message.
- `ServiceRequestTests`: create → approve, create → reject, priority default, creator from token, BR-10, retired vs inactive/under-maintenance equipment, unknown customer/equipment, blank/over-long description and bad priority, reject without a reason, double approve, cross-decisions, concurrent decisions, unknown ids (404), filters and newest-first ordering, invalid filter values (400), history showing real requests.
- `EndpointAccessTests`: `GET`, `GET {id}`, `POST`, `approve`, `reject` × anonymous and five roles = 30 rows.
- `DemoSeedingTests`: seeded request states and idempotency.

**Frontend (Vitest)**
- `LoginPage.test.tsx`: wrong password shows the API message; success stores the session and lands on the dashboard.
- `RoleRouting.test.tsx` (updated in Phase 4 — see below): visitors go to login; only Admin opens Users; Warehouse is bounced from Service Requests; the sidebar offers each role only its pages.
- `ServiceRequests.test.tsx`: list (Manager opens on New, no create button for a Manager; empty and error states), the create form (equipment filtered by customer, retired excluded, exact request body, API refusal shown), Manager approve/reject (reason required), 409 refresh, no buttons for ServiceStaff or on a decided request, 404 state, MYT dates.

**Docker smoke**
- Sign in as all five roles; seeded requests visible; ServiceStaff raises two requests; ServiceStaff cannot approve (403); Technician cannot list (403); another customer's equipment is refused (400); Manager approves and rejects with a reason; a second approval is a 409.

## Phase 4 test inventory

**Backend (xUnit)**
- `WorkOrderStateMachineTests`: all 49 (from, to) pairs against an independently written expectation, plus the refusal message.
- `WorkOrderTests`: AC-01 (assign creates one WorkOrder, second attempt fails, two Managers racing have exactly one winner), AC-02 (assigned technician sees full context and their job list; another technician gets 404 on get/start/mine; office roles can read any job), start/start-twice, diagnosis only while InProgress, AC-05 (blank fields rejected both at the diagnosis PATCH and at Complete), AC-07 (approval locks — further technician edits/complete return 409), approving a non-Completed job is a 409, unknown ids (404), the technicians list with open-job counts, and the full workflow `request → approve → assign → start → diagnose → complete → approve`.
- `EndpointAccessTests`: nine new rows (`assign`, `GET /work-orders`, `GET /work-orders/mine`, `GET /work-orders/{id}`, `start`, `PATCH diagnosis`, `complete`, `approve`, `GET /technicians`) × anonymous and five roles; each row's `Prepare` drives the real endpoints to the precondition its success case needs (e.g. an InProgress job for the diagnosis patch), and ownership-scoped rows reuse the fixture's own technician client rather than an unrelated one.
- `DemoSeedingTests`: work orders now cover Assigned/InProgress/Completed/Approved (one each), the request-status distribution this produces, and that the demo Technician's `mine` list shows only their own jobs.

**Frontend (Vitest)**
- `WorkOrders.test.tsx`: office list and detail (Manager approves a completed job, ServiceStaff sees no button), assigning a technician from the service-request detail page (technician picker shows open-job counts, exact POST body, API refusal shown, no assign UI for ServiceStaff, an already-Assigned request points at Work Orders).
- `TechnicianView.test.tsx`: My Jobs list (lands there at `/`, empty state), job detail (Start, Complete disabled until both fields are filled then saves-and-completes in one tap, read-only once Approved, 404 for another technician's job).
- `RoleRouting.test.tsx`: a Technician visiting any office route lands on My Jobs instead of the dashboard, and renders no office sidebar at all.

**Docker smoke**
- Assign the seeded, still-Approved request to the demo Technician; ServiceStaff cannot assign (403); a second assign of the same request is a 409; the Technician starts, completing without diagnosis/work performed is a 400, saves the diagnosis, completes, and the Manager approves; a WarehouseStaff cannot start a job (403); the approved job then rejects a further complete (409).

## Phase 6 test inventory

**Backend (xUnit)**
- `StockMovementRulesTests` (unit, no database): only Receive/Adjustment are directly creatable, a Receive must be positive, an Adjustment must be non-zero and have a reason, more than 3 decimal places is refused, stock may reach zero but not go below it.
- `InventoryTests`: part created with zero stock and no ledger rows; unique part number (case-insensitive) on create and rename; money/quantity validation; editing a part never changes stock (even with a crafted `quantityOnHand` in the body); stale/missing `rowVersion` on edit (409/400); search and inactive filtering; receive and adjust write exactly one ledger row each; adjustment without a reason, below zero, Issue/Return and malformed movements are refused and change nothing; unknown part (404); inactive parts cannot receive but can be written off; concurrent write-offs and concurrent receives; no route edits or deletes a ledger row; AC-09 low stock (below, at, above, inactive, ordering, leaving the list after a receive); history ordering and filters (part, type, work order, date range) and invalid filters (400). Every stock-moving test ends with a whole-database reconciliation.
- `EndpointAccessTests`: seven new rows (`GET /parts`, `GET /parts/low-stock`, `GET /parts/{id}`, `POST /parts`, `PUT /parts/{id}`, `GET /inventory-transactions`, `POST /inventory-transactions`) × anonymous and five roles — ServiceStaff and Technician get 403 on all of them, Manager on the writes.
- `DemoSeedingTests`: 12 parts / 15 ledger rows, idempotent re-seed, the four low-stock part numbers (the inactive fuse excluded), every seeded movement a Receive or reasoned Adjustment by the warehouse account, and the seed reconciles.

**Frontend (Vitest)**
- `Inventory.test.tsx`: list with RM prices and low-stock highlighting; "Low stock only" uses the low-stock endpoint; empty and error states; a Manager's read-only view; receive (exact body, new stock shown); adjust (disabled until a reason is typed, removal sent negative); a below-zero refusal shown; create sends no stock field; a stale edit's 409 closes the form and reloads; history shows signed quantities, honours `?partId=`, has no buttons, and turns an MYT date range into the right UTC window.
- `RoleRouting.test.tsx`: Inventory appears in the sidebar for Admin, Manager and WarehouseStaff only; ServiceStaff is bounced from `/inventory`, a Technician lands on My Jobs.

**Docker smoke**
- Seeded low stock visible to the warehouse; ServiceStaff cannot read or change inventory and a Technician cannot receive (403); create a part, duplicate number (409), receive, adjustment without a reason (400), adjust with one, going below zero (409), Issue refused (400); the part's ledger sums to its stock and a Manager can read it. (Phase 6 also fixed the earlier walks to send each record's `rowVersion`, which Phase 5 made mandatory.)

## Phase 7 test inventory

**Backend (xUnit)**
- `StockMovementRulesTests` (unit): job quantities must be positive with at most 3 decimals and a sane size; a line total is quantity × snapshot price rounded half away from zero to sen.
- `WorkOrderStateMachineTests` (unit): InProgress ⇄ PendingParts and PendingParts → Cancelled added to the independently written table; start allowed only from Assigned and resume only from PendingParts, for every status; parts change only while InProgress.
- `WorkOrderPartsTests` (integration, real SQL Server): AC-03 and its injected mid-way failures, AC-04, the price snapshot, the concurrent-issue race (five rounds) and the same-screen double tap, return (and returning twice or through another job), ownership, status and lock guards, stale/missing `rowVersion`, invalid quantities, inactive/unknown parts, the technician's active-only catalogue, the office view and the ledger's `workOrderId` filter, and PendingParts/resume. Every stock-moving test ends with a whole-database reconciliation.
- `EndpointAccessTests`: four new rows (`POST /work-orders/{id}/parts`, `DELETE /work-orders/{id}/parts/{lineId}`, `pending-parts`, `resume`) — Technician only, and the fixture's own technician; `GET /parts` now allows a Technician too.
- `DemoSeedingTests`: 6 work orders including the PendingParts belt job; parts on the InProgress, PendingParts and Approved jobs (RM 363.80 on the Approved one); 19 ledger rows — 15 warehouse movements plus 4 Issues by the technician, each linked to its job; the seed still reconciles.

**Frontend (Vitest)**
- `TechnicianView.test.tsx`: pick a part by typing, set quantity with the stepper, add (exact body, no price) and see the RM total; the server's not-enough-stock 409 is shown and the picker stays open; an out-of-stock part offers *Wait for this part…*, which pauses the job (exact body) and shows what it is waiting for with **Resume job**; a refused resume explains why; returning a line sends the job's `rowVersion`; a completed job's parts are read-only.
- `WorkOrders.test.tsx`: the office parts table with RM line totals and job total; a paused job shows what it waits for and can still be cancelled by a Manager.

**Docker smoke**
- A fresh job for the demo technician: the technician can search parts (200) but not read the ledger (403); WarehouseStaff cannot add parts to a job (403); add 2 at the snapshotted price (stock 7 → 5); over-stock is a 409 and changes nothing; return the line (stock back to 7); the job's ledger holds exactly one Issue and one Return; pause for 10; resume refused while short (409); the warehouse receives 3; resume succeeds.

## Phase 8 test inventory

**Backend (xUnit)**
- `SignatureRulesTests` (unit, no database or storage): a real PNG is accepted; empty, over 512 KB (and exactly 512 KB accepted), JPEG, SVG-with-script, PNG signature without IHDR, and zero/absurd dimensions are refused; every upload gets a fresh key under its work order.
- `FileStorageContractTests` (unit against `InMemoryFileStorage`, integration against `AzureBlobFileStorage` on Azurite): save/read back with content type and length, missing key reads as null, delete is idempotent, saving twice under one key is refused and keeps the first file.
- `WorkOrderSignatureTests` (integration, SQL Server + Azurite): AC-06 (refused without a signature, completes once signed; the diagnosis message still comes first); the image round-trips byte for byte as `image/png` with `nosniff`/`no-store` for the technician and every office role, and the DTO never contains the storage key; signing again replaces the image and deletes the old file; unsigned job → 404 on download; non-PNGs refused whatever they claim to be, and a real PNG accepted whatever the browser labels it; over the limit (400) and far over it (cut off before buffering); no file, missing or malformed `rowVersion` (400) — all leaving the job untouched; another technician gets 404 on upload and download; Assigned/Completed jobs are 409 and Approved/Cancelled get the central lock message; with the in-memory fake swapped in: a stale `rowVersion` is a 409 and the uploaded file is deleted again, a storage outage is a 500 without internals and changes nothing, and a double tap has exactly one winner and one stored file.
- `WorkOrderTests`: the Phase 4 completion test and the full-workflow test now sign before completing.
- `EndpointAccessTests`: two new rows — `POST /work-orders/{id}/signature` (the job's own Technician only; multipart body) and `GET /work-orders/{id}/signature` (Admin, Manager, ServiceStaff and the job's own Technician; WarehouseStaff 403).
- `DemoSeedingTests`: exactly the seeded Completed and Approved jobs are signed and their signatures download as valid PNGs; re-running the seeder signs nothing twice.

**Frontend (Vitest)**
- `Signature.test.tsx`: Complete stays disabled until the customer has signed; drawing on the pad and saving uploads `multipart/form-data` (the PNG and the job's `rowVersion`, no hand-set Content-Type), then shows the stored signature and enables Complete; Undo and Clear; a refused upload shows the reason and keeps the strokes; *Sign again* / *Keep the old one*; read-only signature on a completed job; the office page fetches the image with the bearer token, shows *Customer signed* on the timeline, says *Not signed yet* (without fetching) and explains a failed load.
- `TechnicianView.test.tsx`: the diagnosis-and-complete test now starts from a signed job.

**Docker smoke**
- Completing without a signature is a 400; WarehouseStaff cannot upload (403); a non-PNG file declared as `image/png` is a 400; an unsigned job has nothing to download (404); the technician signs (multipart, 200) and the job shows `hasSignature`; complete and approve; the Manager downloads the exact bytes back from Azurite through nginx; WarehouseStaff cannot read it (403); a seeded Completed job's signature downloads as a PNG.

## Phase 9 test inventory

Job photos (PRD §6.6, SDD §15). There is no numbered AC for photos; the plan's Phase 9 test list is covered as below.

**Backend (xUnit)**
- `PhotoRulesTests` (unit, no database or storage): JPEG, PNG and WebP are accepted and typed from their bytes; empty and over 8 MB are refused (exactly 8 MB accepted); SVG-with-script, HTML, GIF, PDF, a non-WebP RIFF (WAV), a PNG signature without IHDR, and a bare JPEG marker are refused; every upload gets a fresh key under its work order with the extension of its detected type.
- `WorkOrderStateMachineTests.Photos_are_added_or_removed_only_while_a_job_is_in_progress` (every status).
- `WorkOrderPhotoTests` (integration, SQL Server + Azurite):
  - **type and size validation** — non-images are refused whatever they claim to be, before anything reaches storage (in-memory fake); a real JPEG labelled `application/octet-stream` / `.HEIC` is accepted and served as `image/jpeg`; over the limit (400/413) and far over it (cut off before buffering); no file (400);
  - **round trip** — JPEG, PNG and WebP round-trip byte for byte with their detected type, `nosniff` and `no-store` for the technician and every office role; the DTO lists them (type, size, uploader) and never contains a storage key; each upload bumps the `rowVersion`;
  - **ownership** — another technician gets 404 on upload, download and remove, and the job is unchanged; a photo is reachable only through its own job;
  - **removal blocked after completion** — removing deletes the row and the file while InProgress; after Complete it is a 409, after Approve the central lock message, and the photo is still downloadable; photos are not required to complete;
  - **state and conflicts** — Assigned, PendingParts and Completed are 409 (*in progress*), Approved and Cancelled get the lock message; at most 20 photos per job; with the in-memory fake: a stale `rowVersion` is a 409 and the uploaded file is deleted again, a storage outage is a 500 without internals, a double tap has exactly one winner and one stored file.
- `EndpointAccessTests`: three new rows — `POST /work-orders/{id}/photos` and `DELETE /work-orders/{id}/photos/{photoId}` (the job's own Technician only) and `GET /work-orders/{id}/photos/{photoId}` (Admin, Manager, ServiceStaff and the job's own Technician; WarehouseStaff 403).
- `DemoSeedingTests`: the seeded InProgress job has one photo and the Completed and Approved jobs two each, all real PNGs in storage; re-running the seeder adds none.

**Frontend (Vitest)**
- `Photos.test.tsx` (the **UI test for the step**; `XMLHttpRequest` replaced by a fake that reports progress): two photos picked together upload one by one — progress bar and percentage, the second *Waiting…*, Complete held as *Uploading photos…* — each with the `rowVersion` the previous upload returned and the bearer token; thumbnails appear. A refused upload shows the reason, **Retry** uploads it again. A 409 reloads the job (here: Cancelled, pickers gone, Retry disabled) and **Discard** drops the failed photo. **Remove** sends the job's `rowVersion`. A completed job shows photos read-only. The office page shows the gallery (images fetched with the token, links to full size, no Remove), *No photos taken on this job*, and a failed image load's message.

**Docker smoke**
- WarehouseStaff cannot upload a photo (403); a shell script declared as `image/jpeg` is a 400; the technician uploads two photos and removes one; after complete and approve, removing it is a 409; the Manager downloads the exact bytes back through nginx and WarehouseStaff cannot (403); a seeded Completed job's two photos are listed and download as PNGs.

## Phase 10 test inventory

The service report PDF (PRD §6.7, SDD §15, BR-14 per D-05). There is no numbered AC for the report; the plan's Phase 10
test list is covered as below.

**Backend (xUnit)**
- `ReportRulesTests` (unit): when a report may be generated — a Completed job any number of times, an Approved job only if it has none yet, never for New/Assigned/InProgress/PendingParts/Cancelled, each with its message; the `SR-{year}-{sequence}` format; a fresh storage key per rendering; MYT across midnight and New Year.
- `ServiceReportBuilderTests` (unit, **report data-model correctness**): report identity and generator in MYT; customer and equipment with blank fields left out; problem, priority, technician, diagnosis, work performed; parts oldest first with line totals rounded to sen and the RM total; photos numbered in the order taken; the timeline in MYT, skipping steps the job never went through and showing a PendingParts wait; empty parts/photos; the job must be loaded with its request, customer and equipment.
- `ServiceReportPdfTests` (unit, the **non-empty PDF smoke check**, made stricter): the file is structurally valid (header, every xref offset points at its object, `startxref`, `%%EOF`); the pages carry the report's text and a numbered footer; long content flows onto more pages with a footer on each; the same input renders the same bytes; photos and the signature are embedded as images (JPEG as-is, PNG as a thumbnail) and a missing photo gets a placeholder; WinAnsi encoding and font metrics; word wrap; the PNG decoder (greyscale/RGB scaling, all five filter types, transparency onto white, 2-bit palette with `tRNS`, 16-bit) and what it refuses (truncated, interlaced, 3.6-gigapixel header, not a PNG); JPEG headers (size, colour, EXIF orientation in both byte orders) and what is refused (CMYK, lossless, arithmetic, no frame header); EXIF orientation applied as the placement matrix.
- `ServiceReportTests` (integration, SQL Server + Azurite):
  - **generate and download** — a Manager generates a numbered report (`SR-{MYT year}-{6 digits}`, revision 1); the Manager, Admin, ServiceStaff and the job's technician download it as `application/pdf`, `SR-….pdf`, `nosniff`/`no-store`; no storage key in the DTO; the PDF pictures the PNG photo, the JPEG photo and the signature;
  - **regeneration blocked after approval** — regenerating a Completed job keeps the number, bumps the revision and deletes the old file (in-memory fake); after Approve it is a 409 with the lock message and the stored PDF is unchanged; a job approved without a report gets exactly one; Assigned, InProgress and Cancelled jobs are 409 and have nothing to download (404); an unknown job is 404;
  - **ownership** — another technician gets 404;
  - **Reports list** — newest first, with the job's customer, equipment, technician, status and lock state;
  - **unique report numbers under parallel generation** — six jobs generated at once get six distinct numbers; five generations of one job at once leave one report row whose revision equals the number of winners, one number, and exactly one stored file (losers and replaced files deleted);
  - **failure** — a storage outage is a 500 without internals and records no report; a photo or signature missing from storage becomes a placeholder, not a failed report.
- `EndpointAccessTests`: three new rows — `POST /work-orders/{id}/generate-report` (Manager only), `GET /work-orders/{id}/report` (Admin, Manager, ServiceStaff and the job's own Technician; WarehouseStaff 403) and `GET /reports` (Admin, Manager, ServiceStaff).
- `DemoSeedingTests`: the seeded Approved job has its locked report (revision 1, generated by the demo Manager) that downloads as a PDF of the recorded size; no other seeded job has one; re-running the seeder generates nothing again.

**Frontend (Vitest)**
- `Reports.test.tsx`: a Manager generates the report of a completed job (no body sent), sees its number, size and generator, and gets Regenerate, the PDF buttons and still Approve; **Download PDF** fetches with the bearer token and saves `SR-….pdf`; **Open PDF** loads it into the tab opened on the click; an approved job's report shows as locked without Regenerate; an approved job without a report still offers Generate; a refused generation shows the reason and reloads the job; ServiceStaff can download but not generate, and are told who makes the report; no report panel before completion. The **Reports page** lists reports with their job link, revision, state (Locked or the job's status) and a Download per row, and has empty and error states; WarehouseStaff and Technicians are kept out of it. The technician can **View report** on their completed job, and is told the Manager makes it while there is none.
- `RoleRouting.test.tsx`: the sidebar now offers **Reports** to Admin, Manager and ServiceStaff.

**Docker smoke**
- A Technician and ServiceStaff cannot generate (403); a job without a report has nothing to download (404); the Manager generates (`SR-…`, revision 1) and regenerates (same number, revision 2); after approval regenerating is a 409; the technician downloads a real PDF through nginx and WarehouseStaff cannot (403); ServiceStaff see it locked in `GET /reports` and WarehouseStaff cannot list (403); the seeded Approved job's locked report downloads as a PDF.

## Phase 11 test inventory

The Manager dashboard (PRD §6.9, SDD §14, AC-09). The plan's Phase 11 test list is covered as below.

**Backend (xUnit)**
- `AnalyticsRulesTests` (unit): the six-month window is whole MYT months ending with the current one, with its UTC bounds; the current month is the MYT month (16:30 UTC on 31 August is September); a window across New Year; the month count is bounded (1–24); missing months are zero and the series is oldest first; every priority (most urgent first) and every status (lifecycle order) is present; the headline tiles are derived from the series.
- `AnalyticsTests` (integration, real SQL Server; **each operation over a small deterministic dataset with a known answer**, each test in its own scratch database with a fixed clock):
  - **empty data** — every series complete and zero, no low-stock items, all tiles zero;
  - `OpenRequestsByPriority` — only New and Approved count (Assigned, Rejected, Cancelled do not);
  - `WorkOrdersByStatus` — per status with Cancelled included, and the active/awaiting/this-month tiles;
  - `JobsCompletedPerMonth` — **month boundaries in MYT**: 23:59:59 and 00:00 MYT on either side of 1 September, the first instant of the window and the second before it, the first instant after it, a Completed (not yet approved) job counted, a Cancelled job with a timestamp not counted; and a trend across New Year;
  - `LowStockSummary` — at-minimum counts, inactive never counts, out of stock, most urgent first, top-N, and the **same parts in the same order as `GET /parts/low-stock`** (AC-09);
  - AC-09 through the API — a part created below its minimum raises the Manager's low-stock count by one and leads the list.
- `DemoSeedingTests`: **the dashboard equals the hand-computed values of the seed** (priorities 1/2/2/0, statuses 0/1/1/1/1/9/1, four parts low with one out of stock, the tiles, and a monthly trend checked against the seeded jobs' own `CompletedAt` bucketed independently, spanning at least four months); the **latency sanity check** — the median of five warm summary calls on the seed is under 500 ms (NFR-03); the eight history jobs are approved, signed, reported and in lifecycle order; the earlier counts updated for them.
- `EndpointAccessTests`: one new row — `GET /dashboard/summary` (Admin, Manager).

**Frontend (Vitest)**
- `Dashboard.test.tsx`: a Manager sees the server's tiles (linked to their lists) and the charts as labelled tables and a list of six months with readable values; the low-stock panel highlights the out-of-stock part; empty series read as sentences; a failed load shows an alert and **Refresh** recovers; ServiceStaff keep the simple counts and never call the endpoint.

**Docker smoke**
- The Manager's summary has every priority, every status and six months; its low-stock count equals `GET /parts/low-stock`; its statuses add up to every work order; its open requests equal the New plus Approved lists; the seeded history spans at least four months; ServiceStaff, a Technician and WarehouseStaff get 403.

## Running

```bash
dotnet test                              # backend: unit + integration + authorization (needs Docker)
cd src/SarawakBizOps.Web && npm test     # frontend
docker compose up -d --build --wait && bash scripts/compose-smoke.sh   # smoke (needs curl, jq, base64, cmp, od)
```
