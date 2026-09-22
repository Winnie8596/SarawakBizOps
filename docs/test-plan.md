# Test plan and acceptance-criteria map

Every acceptance criterion (AC-01 … AC-11, from the System Design Document) must end up with at least one named,
passing automated test. The map below grows a phase at a time (see [`plan.md`](../plan.md)); Phase 15 only verifies it
and fills gaps. A blank "Test" cell means the criterion is not implemented yet.

## How the tests are organised

| Layer | Where | What it proves |
|---|---|---|
| Unit | `tests/SarawakBizOps.Api.Tests` (`*StateMachineTests`) | Pure rules with no database: every lifecycle transition, allowed and forbidden |
| Integration | `tests/SarawakBizOps.Api.Tests` | The real API against a real SQL Server container (Testcontainers): business rules, validation, concurrency, seed data |
| Authorization | `EndpointAccessTests` | Every endpoint × (anonymous + all five roles) → exactly 401, 403 or the success status (AC-11) |
| Frontend | `src/SarawakBizOps.Web/src/test` (Vitest + React Testing Library) | The UI honours role gating and shows the API's messages; `fetch` is stubbed, the components are real |
| Docker smoke | `scripts/compose-smoke.sh` (CI job `compose-smoke`) | The shipped containers work together: sign-in through nginx, seed data, request intake |

Conventions: each integration test creates its own uniquely named customers/users, so tests never depend on run order;
the `Api` collection shares one SQL Server container per test run.

## Acceptance-criteria map

| AC | Statement (short) | Phase | Test(s) | Status |
|---|---|---|---|---|
| AC-01 | Assign creates exactly one WorkOrder; request marked assigned | 4 | `WorkOrderTests.Assigning_an_approved_request_creates_one_work_order_and_marks_it_assigned`, `Assigning_the_same_request_twice_fails_on_the_second_attempt`, `Two_managers_racing_to_assign_the_same_request_have_exactly_one_winner` | **Done** |
| AC-02 | Technician sees customer, equipment, problem, priority, status | 4 | `WorkOrderTests.The_assigned_technician_sees_full_job_context_and_it_appears_in_their_job_list`, `Another_technician_cannot_read_or_act_on_someone_elses_job` | **Done** |
| AC-03 | Add part → one WorkOrderPart + one Issue transaction, atomically | 7 | | Not yet implemented |
| AC-04 | Insufficient stock rejected, stock unchanged | 7 | | Not yet implemented |
| AC-05 | Completion rejected without diagnosis / work performed | 4 | `WorkOrderTests.Completing_without_diagnosis_or_work_performed_is_rejected`, `Saving_a_diagnosis_with_a_blank_field_is_rejected_and_the_job_still_cannot_complete` | **Done** |
| AC-06 | Completion rejected without a signature | 8 | | Not yet implemented |
| AC-07 | Manager approval locks the work order | 4, 5 | `WorkOrderTests.Approving_a_completed_job_locks_it_against_further_technician_action` (status changes); `WorkflowCancellationTests.A_technician_cannot_edit_the_diagnosis_of_an_approved_job` (the central lock guard also covers non-transition edits) | **Done** |
| AC-08 | Technician edit after approval rejected | 5 | `WorkflowCancellationTests.A_technician_cannot_edit_the_diagnosis_of_an_approved_job` | **Done** |
| AC-09 | Low-stock parts appear on the dashboard | 6, 11 | | Not yet implemented |
| AC-10 | Manager AI uses backend-computed data only | 14 | | Not yet implemented |
| AC-11 | Unauthorized role rejected regardless of the UI | every phase | `EndpointAccessTests.Each_endpoint_allows_only_its_roles` (rows: the six `/api/service-requests` + eleven work-order/technician endpoints, including both Phase 5 `/cancel` routes); `EquipmentAndHistoryTests` (equipment update, history), `UserManagementTests` (users) for the Phase 1 endpoints | **In progress**: rows are added with each phase; Phase 15 verifies the whole surface |

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
| Demo data is consistent and idempotent | `DemoSeedingTests` (11 requests and 5 work orders in the right states, including one Cancelled of each, BR-10 holds for every seeded row, re-seeding changes nothing) |
| BR-01 (assignment half), BR-13 request follows the job | `WorkOrderTests` full-workflow test; `ServiceRequestStateMachineTests`/`WorkOrderTests` cover the two halves separately since assignment moves the *request*, not the work order |
| BR-02 a technician acts only on their own job | `WorkOrderTests.Another_technician_cannot_read_or_act_on_someone_elses_job` (get, start and `mine` all checked); ownership is reported as 404, not 403, so a technician cannot even confirm another's job exists |
| BR-11 only an active Technician can be assigned | `WorkOrderTests.Assigning_to_a_non_technician_or_inactive_technician_is_rejected` |
| BR-12 one *live* WorkOrder per request | `WorkOrderTests.Assigning_the_same_request_twice_fails_on_the_second_attempt`, `Two_managers_racing_to_assign_the_same_request_have_exactly_one_winner`; `WorkflowCancellationTests.A_manager_cancels_an_assigned_job_and_the_request_becomes_reassignable` (the filtered unique index on `WorkOrder.ServiceRequestId`, excluding `Cancelled` rows, is the final guard both ways) |
| Work-order lifecycle guards | `WorkOrderStateMachineTests` (all 49 (from, to) pairs, including the three new Cancel targets); `WorkOrderTests.Starting_a_job_twice_is_a_409`, `Approving_a_job_that_is_not_completed_is_a_409` |
| BR-06/AC-08 central lock guard (Phase 5) | `WorkflowCancellationTests.A_technician_cannot_edit_the_diagnosis_of_an_approved_job` (a locked job refuses a non-transition edit, not just a status change, via `WorkOrderStateMachine.IsLocked`) |
| Cancellation (Phase 5) | `WorkflowCancellationTests` — request cancel by creator-side roles (New only) and by a Manager (New or Approved); work-order cancel by a Manager (New/Assigned/InProgress) with the request-revert side effect; both require a reason |
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

## Running

```bash
dotnet test                              # backend: unit + integration + authorization (needs Docker)
cd src/SarawakBizOps.Web && npm test     # frontend
docker compose up -d --build --wait && bash scripts/compose-smoke.sh   # smoke (needs curl and jq)
```
