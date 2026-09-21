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
| AC-01 | Assign creates exactly one WorkOrder; request marked assigned | 4 | | Not yet implemented |
| AC-02 | Technician sees customer, equipment, problem, priority, status | 4 | | Not yet implemented |
| AC-03 | Add part → one WorkOrderPart + one Issue transaction, atomically | 7 | | Not yet implemented |
| AC-04 | Insufficient stock rejected, stock unchanged | 7 | | Not yet implemented |
| AC-05 | Completion rejected without diagnosis / work performed | 4 | | Not yet implemented |
| AC-06 | Completion rejected without a signature | 8 | | Not yet implemented |
| AC-07 | Manager approval locks the work order | 4, 5 | | Not yet implemented |
| AC-08 | Technician edit after approval rejected | 5 | | Not yet implemented |
| AC-09 | Low-stock parts appear on the dashboard | 6, 11 | | Not yet implemented |
| AC-10 | Manager AI uses backend-computed data only | 14 | | Not yet implemented |
| AC-11 | Unauthorized role rejected regardless of the UI | every phase | `EndpointAccessTests.Each_endpoint_allows_only_its_roles` (rows: the five `/api/service-requests` endpoints); `EquipmentAndHistoryTests` (equipment update, history), `UserManagementTests` (users) for the Phase 1 endpoints | **In progress**: rows are added with each phase; Phase 15 verifies the whole surface |

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
| Demo data is consistent and idempotent | `DemoSeedingTests` (6 requests in the right states, BR-10 holds for every seeded row, re-seeding changes nothing) |

## Phase 3 test inventory

**Backend (xUnit)**
- `ServiceRequestStateMachineTests`: all 25 (from, to) pairs against an independently written expectation, plus the refusal message.
- `ServiceRequestTests`: create → approve, create → reject, priority default, creator from token, BR-10, retired vs inactive/under-maintenance equipment, unknown customer/equipment, blank/over-long description and bad priority, reject without a reason, double approve, cross-decisions, concurrent decisions, unknown ids (404), filters and newest-first ordering, invalid filter values (400), history showing real requests.
- `EndpointAccessTests`: `GET`, `GET {id}`, `POST`, `approve`, `reject` × anonymous and five roles = 30 rows.
- `DemoSeedingTests`: seeded request states and idempotency.

**Frontend (Vitest)**
- `LoginPage.test.tsx`: wrong password shows the API message; success stores the session and lands on the dashboard.
- `RoleRouting.test.tsx`: visitors go to login; only Admin opens Users; Technician and Warehouse are bounced from Service Requests; the sidebar offers each role only its pages.
- `ServiceRequests.test.tsx`: list (Manager opens on New, no create button for a Manager; empty and error states), the create form (equipment filtered by customer, retired excluded, exact request body, API refusal shown), Manager approve/reject (reason required), 409 refresh, no buttons for ServiceStaff or on a decided request, 404 state, MYT dates.

**Docker smoke**
- Sign in as all five roles; seeded requests visible; ServiceStaff raises two requests; ServiceStaff cannot approve (403); Technician cannot list (403); another customer's equipment is refused (400); Manager approves and rejects with a reason; a second approval is a 409.

## Running

```bash
dotnet test                              # backend: unit + integration + authorization (needs Docker)
cd src/SarawakBizOps.Web && npm test     # frontend
docker compose up -d --build --wait && bash scripts/compose-smoke.sh   # smoke (needs curl and jq)
```
