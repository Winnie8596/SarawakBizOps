# SarawakBizOps — Implementation Plan (Tracer-Bullet Edition)

| | |
|---|---|
| **Derived from** | [`prd.md`](./prd.md) (Draft v1.0, 2026-09-21) and *System Design Document v1.1 (Reviewed)* |
| **Method** | **Tracer bullets** (*The Pragmatic Programmer*): every phase is a thin **vertical slice** through *all* layers — data → service → route → UI → tests — that ends in something you can click through. Early phases are the thinnest possible path across the whole system; later phases thicken it. |
| **Supersedes** | The earlier horizontal plan (API phases, then UI phases, then "hardening"). PRD §10's milestone table (M0–M11) is superseded by this plan's phase order. |
| **Capacity assumed** | 20+ hrs/week |
| **Working loop** | Failing end-to-end test → smallest service → route → UI → run it → thicken → commit → tag (SDD §24) |

## The rule that shapes everything

A tracer bullet is **production code, not a throwaway prototype**. It is deliberately *thin* (one happy path, few fields, minimal rules) but *real*: real database, real authorization, real UI, real tests. Nothing is stubbed behind a flag that later has to be ripped out.

**A phase is a vertical slice only if it has every one of these:**

| Layer | What must exist at the end of the phase |
|---|---|
| **Data** | Any migration it needs (reviewed before applying) and the seed rows that show the feature off |
| **Service** | The business rule in a service / pure class (state machines are unit-testable without a DB) |
| **Route** | The endpoint(s), role-restricted, returning the standard ProblemDetails errors |
| **UI** | A page or step a real role can use, with loading, empty and error states (technician steps designed for 360 px from the start) |
| **Tests** | Unit tests for the rule, an integration test of the happy path against real SQL Server, **authorization rows** for every new endpoint, and at least one UI test for role gating or the new flow |
| **Ops & docs** | `docker compose up` still works, CI green, README + `docs/test-plan.md` (AC map) updated |

If a phase can't show a **demo script** (a short click-through a stranger could follow), it is too horizontal — reslice it.

### Cross-cutting work is spread across slices, not saved for the end

| Concern | Where it lives now |
|---|---|
| Authorization tests (AC-11) | One table-driven `EndpointAccessTests` file. **Each phase adds rows** for its endpoints; there is no big "matrix" phase at the end. |
| Seed data | Each phase adds the rows for its lifecycle states, so the demo dataset grows with the product. |
| Docker / CI | Docker compose arrives in **Phase 2** and every later phase must keep it working. |
| Structured logs, timestamps (NFR-08) | Added with the transition or operation they describe. |
| Loading / empty / error / 409 UI states | Part of each page's definition of done. |
| README / test plan | Updated in each phase; Phase 15 only verifies and adds screenshots and video. |
| Security review | Each phase reviews its own new surface; Phase 15 does one final sweep. |

### Conventions (apply in every phase)

- Backend layering: `Controller (thin) → Service (rules, transactions) → ApplicationDbContext`. DTOs only, manual mapping, `CancellationToken` and async everywhere. Expected failures return `ServiceResult` and are mapped by `ToProblem`.
- Authorization: `[Authorize(Roles=…)]` **plus** ownership checks in services. The UI hides, the API enforces.
- Errors: RFC 7807 ProblemDetails everywhere (400/422, 401, 403, 404, 409). No stack traces to clients.
- Frontend: follow the `CustomersPage.tsx` shape (list + form + role-gated actions). Dates shown in MYT, money as RM.
- New dependencies only when a requirement demands them (PRD D-06); each is recorded in the README "Dependencies & why".
- Tests use real SQL Server via Testcontainers (`ApiFactory`); each test creates uniquely-named data, so tests never depend on run order.
- Commit per task group; tag `phase-N-done` when the exit criteria pass.

---

## Phase overview

| # | Vertical slice (what a user can newly do) | Est. hrs | Status |
|---|---|---|---|
| 0 | **Walking skeleton:** sign in, see customers and equipment; CI and integration tests exist | done | ✅ Done (`phase-0-done`) |
| 1 | **Users & master data:** Admin manages users and roles; edit equipment; see history | done | ✅ Done (`phase-1-done`) |
| 2 | **Deployable skeleton:** `docker compose up` gives a seeded, working system | done | ✅ Done (`phase-2-done`) |
| 3 | **Request intake:** staff raise a service request, Manager approves or rejects it | 18–22 | 🟡 Built and green locally; tag `phase-3-done` once CI is green on `main` |
| 4 | **First job, end to end:** assign → technician starts and completes → Manager approves | 25–30 | Next |
| 5 | **Workflow rules for real:** cancel, locks, ownership, conflicts, status timeline | 20–25 | |
| 6 | **Parts & stock in:** warehouse manages the catalogue, receives stock, sees low stock | 18–22 | |
| 7 | **Parts used on a job:** technician adds parts (atomic issue), pending-parts, concurrency proof | 25–30 | |
| 8 | **Customer signature:** drawn on the phone, stored, required to complete | 15–20 | |
| 9 | **Job photos:** capture, upload, gallery | 12–16 | |
| 10 | **Service report PDF:** generate, lock, download, Reports page | 20–25 | |
| 11 | **Manager dashboard (core metrics)** | 15–18 | |
| 12 | **Analytics depth:** remaining metrics, role-specific dashboards | 12–16 | |
| 13 | **AI Technician Assistant** (mock-first) | 18–22 | |
| 14 | **AI Manager Insights** (grounded in analytics) | 15–20 | |
| 15 | **Release polish:** final sweeps, docs, demo video, public flip | 20–25 | |
| 16 | **Stretch:** React Native tracer, live deploy | open | P2 |

Remaining P0/P1 work ≈ **245–310 hrs** — about 12–16 weeks at 20 hrs/week. After **Phase 4 the whole workflow already works end to end** (thinly); everything after that deepens it.

```
Phase 3            Phase 4
Request ──approve──► Assign ──► Work order ──► Technician ──► Manager approves
 (thin)               (thin, atomic)            start/complete     (locks)
   │                     │                          │
   └─────── Phases 5–10 thicken this same line: ────┘
   rules & timeline · parts & stock · signature · photos · PDF report
   Phases 11–14 add what sits beside it: dashboard · AI
```

---

## ✅ Phase 0 — Walking skeleton (done)

**Demo:** clone, run, sign in as the seeded admin, list/create customers and equipment; CI is green.
**Delivered:** git history, MIT license, solution + xUnit project with a Testcontainers SQL Server `ApiFactory`, CI (backend build + tests, web build), RFC 7807 errors end to end (API + `apiFetch` + 401 → login), JWT-key guard, README.
**Repo:** private until the owner says otherwise (PRD D-07 flip is Phase 15).

## ✅ Phase 1 — Users & master data (done)

**Demo:** Admin creates a user per role and each signs in; a deactivated user is kicked out at once; ServiceStaff edits equipment status; a customer page shows its equipment and history.
**Covers:** PRD §6.1, §6.2 · BR-07, BR-08, BR-11 (groundwork) · AC-11 (start).

| Layer | Delivered |
|---|---|
| Data | none (no migration) |
| Service | `UserService` (single role per user, self-lockout guard), security-stamp token revocation (`JwtUserValidator`), change-password, admin reset, `EquipmentService.UpdateAsync` (customer can't change), `ServiceHistoryService` |
| Route | `/api/users` (Admin), `POST /api/auth/change-password`, `PUT /api/equipment/{id}`, `GET /api/{customers,equipment}/{id}/history` (office roles) |
| UI | Users page, change-password page, equipment edit + status, customer/equipment detail pages with history, role-aware sidebar and route guards |
| Tests | 48 integration tests (role matrix, revocation, lockout, validation, history over real rows) |

**Closed:** manual UI check passed (users per role, role-aware sidebar, deactivation signs the user out, reset and change password). The check found two UX gaps, both fixed: the Users page now explains why you can't edit or deactivate your own account, and list pages have a visible **View & history** link.
**Carried forward:** MYT display of history timestamps could not be seen with an empty history; verify it in Phase 3 when service requests appear.

---

## ✅ Phase 2 — Deployable skeleton (done)

**Goal:** the architecture is proven *deployable* before more features pile on. `docker compose up` on a clean machine gives a working, seeded system, and CI proves it on every push. This is the tracer for the infrastructure layer.
**Demo:** clone into an empty folder, run `docker compose up`, open the web app, sign in as each demo role, see sample customers and equipment.
**Covers:** PRD §9 (Docker, seed, CI), Q-03 · D-03 (storage emulator arrives in Phase 8).

| Layer | Work |
|---|---|
| Data | Migrations applied **on startup in demo mode** (configuration flag); idempotent `DemoSeeder`: one account per role (demo-only passwords, documented), ~5 customers across Kuching/Sibu/Miri, ~10 equipment items |
| Service | Split `DbSeeder` into always-on (roles + admin) and opt-in demo seeding; config-driven, no secrets in images |
| Route | A `/health` endpoint for compose health checks (unauthenticated, no data) |
| UI | Web container serves the built app (nginx) and proxies `/api` to the API, so there is no CORS or base-URL setup for reviewers; the browser calls a relative `/api`, fixed at image build time |
| Tests | Integration test: demo seeding is idempotent and creates one user per role; **CI job** starts `docker compose up`, waits for health, logs in through the web container and calls `/api/customers` |
| Ops & docs | Multi-stage Dockerfiles (API, Web), `docker-compose.yml` (sqlserver, api, web) with health checks and a volume, `.env.example` (no real secrets), README "Run with Docker" first, "manual dev" second, demo-accounts table marked demo-only |

**Decisions taken (grilled before building)**

| Topic | Decision |
|---|---|
| Web → API | Relative `/api` baked in at build time; nginx proxies to `api:8080`; same origin, so no CORS. HTTPS redirect is switched off by `Hosting:UseHttpsRedirection=false` in compose only. |
| Migrations | `Database:MigrateOnStartup` (default **off**, on only in compose): `Migrate()` with a bounded retry while SQL Server warms up, then the seeders, all before Kestrel listens. |
| Demo passwords | One shared demo password (`Seed:DemoPassword`, default `Demo!2026` in compose). Demo accounts exist only when `Seed:Demo=true`. The hardcoded `ChangeMe123!` fallback is gone: outside Development and demo mode the API fails to start without `Seed:AdminPassword`. |
| Secrets | Labelled demo defaults for `JWT_KEY` and `MSSQL_SA_PASSWORD` in compose, overridable via `.env`. |
| Health | `/health` (liveness) and `/health/ready` (database). Compose and CI wait on `/health/ready`. |
| Seeding | Per-row natural keys (email, company name, serial number), insert-if-missing. Reset with `docker compose down -v`. |
| CI | Separate `compose-smoke` job on push and PR, with buildx layer caching; dumps container logs on failure. |

**Exit criteria**
- [x] A fresh clone → `docker compose up` → sign in works, with no LocalDB and no manual steps. *(Verified locally from an empty volume: all three containers healthy in about 47 s once images were built; smoke script passes for all five roles.)*
- [x] CI compose smoke job is green. *(First run on `main` passed: backend, web and compose smoke jobs.)*
- [x] Every later phase's demo script is written to be run against this Docker setup. *(Rule recorded; the README documents the demo accounts and reset command.)*

---

## Phase 3 — Request intake *(tracer, part 1 of 2)*

**Goal:** the first business object flows through every layer: ServiceStaff raise a service request; a Manager reviews it. Deliberately *thin*: create, list, approve, reject. Cancel and edge rules wait for Phase 5.
**Demo:** ServiceStaff picks a customer and one of *their* equipment, describes the fault, submits. The Manager sees it in a queue, approves one and rejects another with a reason. Status badges update immediately.
**Covers:** PRD §6.3 · BR-01 (partial), BR-10 · AC-11 rows for these endpoints.

| Layer | Work |
|---|---|
| Data | Migration: `ServiceRequest.RejectionReason` (reviewed). Seed: requests in `New`, `Approved`, `Rejected`. |
| Service | `ServiceRequestStateMachine` (pure): `New→Approved`, `New→Rejected`. `ServiceRequestService`: create (**equipment must belong to customer — BR-10**, `CreatedByUserId` from the token), list with filters (status, priority, customer), get, approve (`ApprovedBy`/`ApprovedAt`), reject (reason required). Structured log per transition. |
| Route | `GET/POST /api/service-requests`, `GET /{id}`, `POST /{id}/approve`, `POST /{id}/reject`. Create: ServiceStaff/Admin; approve/reject: Manager. |
| UI | Service Requests page: list with status/priority badges and filters; create form (customer picker → equipment picker filtered by that customer); detail with **state-aware** Approve / Reject (with reason) for Managers. Extend `StatusBadge`. Customer/equipment history now shows real requests. |
| Tests | State-machine unit tests (every valid and invalid transition). Integration: happy path create → approve; BR-10 rejection (other customer's equipment); reject requires a reason; invalid transition → 409. `EndpointAccessTests` created with these rows (ServiceStaff cannot approve, Technician/Warehouse cannot create). **Frontend test setup introduced here** (Vitest + React Testing Library, PRD D-06 test tooling): login, protected/role routes, Users-page and Service-Requests role gating. |
| Ops & docs | Seed and README updated; `docs/test-plan.md` created with the AC map (AC rows fill in as phases land); CI runs frontend tests. |

**Decisions taken (grilled before building)**

| Topic | Decision |
|---|---|
| Who reads requests | Admin, Manager and ServiceStaff can list and open **every** request. Technician and Warehouse get 403 (a Technician sees only their own work orders from Phase 4, BR-02). |
| Reviewer audit | The migration adds `RejectionReason`, `RejectedByUserId` and `RejectedAt` (not just the reason), so a rejection records who and when, like an approval does. `Approved*` columns are not reused. |
| Equipment rule | BR-10 ownership is enforced; **Retired** equipment is also refused (400). Active, Inactive and UnderMaintenance are allowed. |
| Concurrency | No `RowVersion` on requests yet. Approve/reject is one conditional `UPDATE … WHERE Status = 'New'`; the loser of a race updates 0 rows and gets a 409. Phase 5's conflict mechanism (Open Question 9) still decides how work orders do it. |
| UI shape | `/service-requests` list + `/service-requests/:id` detail (same shape as Customers/Equipment). A Manager opens the list pre-filtered to **New**, which is their queue. |
| Frontend tests | Vitest + React Testing Library + jsdom; `fetch` stubbed by hand (no MSW). Vitest 2 is kept despite a dev-only `npm audit` advisory (needs Vite 6+); recorded in the README and left for the Phase 15 sweep. |
| Demo seed | 6 requests over ~3 weeks: 3 New (one Urgent), 2 Approved, 1 Rejected with a reason. No Assigned/Cancelled rows until those states are reachable. |
| Git | Commits per task group on `main`; nothing pushed by the assistant. |

**Also done in this phase:** history rows link to the request detail page; the form hints sit outside their `<label>` (linked with `aria-describedby`) so the field names stay clean. *Smoke-script note:* a body-less `curl -X POST` sends no `Content-Length` and Kestrel answers a bare 400, whereas a browser `fetch` POST always sends `Content-Length: 0`; the script therefore sends an empty body on POSTs, and the product needed no change.

**Exit criteria**
- [x] The demo script works in the Docker setup. *(Fresh volume, all three containers healthy; `compose-smoke.sh` passes, run twice against the same volume, including the request-intake walk. The UI half of the script is covered by the Vitest flow tests; a by-hand click-through in a browser is still worth doing once.)*
- [x] Invalid transitions return 409/400 with a clear message that the UI shows. *(API tests assert the messages; the UI test shows the 409 text and refreshes.)*
- [ ] Backend and frontend tests are green in CI. *(Both suites are green locally, 165 backend and 28 frontend tests, and the concurrency test passed 4 repeat runs. CI itself has not run because nothing has been pushed; tag `phase-3-done` after the first green run.)*

**Carried forward:** the MYT display of history timestamps is now asserted by a test (02:30 UTC shows as 10:30) and the seeded requests give history something to show; eyeball it once in the browser.

---

## Phase 4 — First job, end to end *(tracer, part 2 of 2)*

**Goal:** complete the thinnest possible path across the *whole* workflow — Request → Approval → **Assignment → Work Order → Technician → Manager approval**. After this phase the product does its core job; the rest is depth. Signature, parts, photos, PDF and cancellation are **not** here.
**Demo:** Manager assigns an approved request to a technician (one click, atomic). On a phone-sized screen the technician sees *My Jobs*, opens the job, taps Start, types diagnosis and work performed, taps Complete. The Manager sees the completed order and approves it; it is then read-only.
**Covers:** PRD §6.4 · SDD §9 · BR-01, 02, 03 (partial), 06, 11, 12, 13 · **AC-01, 02, 05, 07** (AC-06 signature lands in Phase 8; AC-08 rejects edits after approval — first cut here, hardened in Phase 5).

| Layer | Work |
|---|---|
| Data | No new tables (entities exist). Unique index on `WorkOrder.ServiceRequestId` is verified (BR-12). Seed: jobs in `Assigned`, `InProgress`, `Completed`, `Approved`. |
| Service | `WorkOrderStateMachine` (pure, minimal): `Assigned→InProgress→Completed→Approved`. **Atomic assign** in one DB transaction: request is `Approved` → user is an *active Technician* (BR-11) → no existing work order (BR-12) → create WorkOrder + mark request `Assigned` (BR-13). Technician actions check **ownership** (BR-02). Complete requires diagnosis + work performed (BR-03 partial). `RowVersion` returned in DTOs. Structured log on every transition. |
| Route | `POST /api/service-requests/{id}/assign`, `GET /api/work-orders` (office), `GET /api/work-orders/mine` (Technician), `GET /{id}`, `POST /{id}/start`, `PATCH /{id}/diagnosis`, `POST /{id}/complete`, `POST /{id}/approve`, `GET /api/technicians` (with open-job counts). |
| UI | **Assign dialog** on an approved request (technician picker with open-job count). **Work Orders** page + detail (office). **Technician view** under `/tech/*` — chosen automatically for the Technician role, **designed for 360 px, 44 px tap targets, sticky primary action**: My Jobs → Job Details → Start → Diagnosis / Work Performed → Complete. Manager Approve button. |
| Tests | State-machine unit tests. Integration: **AC-01** (assign creates exactly one work order; second assign fails), **AC-02** (assigned technician reads the job; another technician gets 403/404), **AC-05** (complete rejected without diagnosis/work performed), **AC-07** (approval locks), and the **full-workflow test** `request → approve → assign → start → complete → approve`. Access rows for every new endpoint. UI test: Technician lands on `/tech`, sees only their jobs. |
| Ops & docs | Seed and README updated; demo accounts can walk the full flow; test-plan AC map filled for AC-01/02/05/07. |

**Exit criteria**
- [ ] The demo script works, on the technician side at 360 px.
- [ ] The full-workflow integration test is green in CI.
- [ ] Everything works in the Docker setup.

> **Milestone:** the tracer has reached the far end. From here on, each phase deepens one part of an already-working line.

---

## Phase 5 — Workflow rules for real

**Goal:** turn the thin line into a trustworthy state machine: every transition guarded, every conflict visible, every state explained.
**Demo:** a Manager cancels an assigned job with a reason; a technician tries to edit an approved job and is told it is locked; two Managers act on the same order at once and the second sees "This record changed — refresh"; the order page shows a status timeline.
**Covers:** PRD §6.3–6.4 · SDD §8–9 · BR-02, 06, 13 · **AC-08** (complete), AC-11 rows.

| Layer | Work |
|---|---|
| Data | Migration: cancellation reason columns and transition-timestamp fields the timeline needs (reviewed). |
| Service | Full transition tables from SDD §8–9 with actor role + guard predicates. Request cancel (`New/Approved→Cancelled`); work-order cancel (`New/Assigned/InProgress`, reason). **One central lock guard**: any mutating call on `Approved`/`Cancelled` → 409 (not scattered `if`s). `RowVersion` conflicts (`DbUpdateConcurrencyException`) → 409 "reload and retry"; pick one mechanism (`If-Match` header or body field) and document it. Cancelling a work order returns its request to a defined state (decide and document). |
| Route | `POST /api/service-requests/{id}/cancel`, `POST /api/work-orders/{id}/cancel`; state-changing calls accept the `RowVersion`. |
| UI | Cancel actions with reason; buttons appear **only for valid transitions**; **status timeline** on work-order detail; a shared "record changed — refresh" conflict banner used by every page; locked-order read-only display. |
| Tests | Table-driven tests for *every* transition including forbidden ones; **AC-08** (technician edit after approval rejected); concurrency test (two simultaneous status changes → one succeeds, one 409); cancel paths; access rows for the new endpoints. |
| Ops & docs | Workflow state diagrams (request + work order) drafted for the README; seed gets `Cancelled` examples. |

**Exit criteria**
- [ ] No mutation path bypasses the lock guard (a test enumerates the mutating endpoints).
- [ ] The concurrency and conflict UI works in a manual two-browser check.

---

## Phase 6 — Parts & stock in

**Goal:** the inventory ledger exists and a warehouse user can run it. Deliberately *not* yet connected to jobs.
**Demo:** Warehouse adds parts, receives a delivery, applies a stock adjustment with a reason, sees the low-stock list and the transaction history; other roles cannot touch inventory.
**Covers:** PRD §6.5 (first half) · SDD §10 · BR-04 (groundwork), BR-09 · **AC-09 (API)**.

| Layer | Work |
|---|---|
| Data | No new tables (entities exist); review the `Part` / `InventoryTransaction` configuration and add indexes needed by the history filters (reviewed migration if any). Seed: a realistic parts catalogue, several below minimum, with receive transactions so the ledger reconciles. |
| Service | `PartService`: CRUD, unique `PartNumber`, decimal quantities, `MinimumStockLevel`, `IsActive`, low-stock query (`QuantityOnHand <= MinimumStockLevel`, active only). `InventoryService`: **Receive** (+) and **Adjustment** (signed, **mandatory reason**). Every stock change writes exactly one **immutable** ledger row in the same transaction as the `QuantityOnHand` update (BR-09). `Issue`/`Return` are *not* creatable through the API — they come only from work-order part operations (Phase 7). |
| Route | `GET/POST /api/parts`, `GET/PUT /api/parts/{id}`, `GET /api/parts/low-stock`, `POST /api/inventory-transactions`, `GET /api/inventory-transactions` (filters: part, type, work order, date range). No update or delete endpoints for transactions. |
| UI | **Inventory** page: parts list with search and low-stock highlighting (RM shown), part form, Receive / Adjust dialogs (reason required), **transaction history** with filters. |
| Tests | **AC-09** at API level; reconciliation helper (`QuantityOnHand == SUM(ledger)`) used by tests; adjustment without a reason rejected; no code path edits or deletes a transaction; access rows (ServiceStaff cannot change inventory, Technician cannot Receive/Adjust, Warehouse cannot assign jobs). UI tests for role gating. |
| Ops & docs | Seed + README; test-plan updated. |

**Exit criteria**
- [ ] Stock always reconciles with the ledger after the test suite.
- [ ] Demo script works in Docker.

---

## Phase 7 — Parts used on a job

**Goal:** the hardest rule in the product, delivered as one slice: a technician consumes stock from a work order **atomically and concurrency-safely**.
**Demo:** a technician adds two parts to an in-progress job; stock drops and the order shows parts with an RM total. Adding more than is in stock is refused. Two technicians racing for the last unit: exactly one wins. A job is paused as *Pending parts* and resumed when stock exists.
**Covers:** PRD §6.5 (second half) · SDD §10, §18 · BR-04, 05, 13 · **AC-03, AC-04**.

| Layer | Work |
|---|---|
| Data | Seed: jobs with parts used, one in `PendingParts`. |
| Service | **Add part to work order** in **one DB transaction**: re-read the part with concurrency protection, check `Quantity <= QuantityOnHand` (BR-04), create `WorkOrderPart` (price snapshot from `Part.UnitCost`), create exactly one `Issue` ledger row linked to the order (BR-05), decrement stock. Never trust client price. **Return/remove part** creates a `Return` row. Concurrency strategy chosen and documented (bounded `RowVersion` retry *or* `UPDLOCK`). `PendingParts` and `resume` transitions added to the state machine; resume is guarded by stock availability. |
| Route | `POST /api/work-orders/{id}/parts`, `DELETE /api/work-orders/{id}/parts/{partId}` (return), `POST /{id}/pending-parts`, `POST /{id}/resume`. Assigned technician only, editable states only. |
| UI | Technician step **Parts** (search + quantity stepper, large tap targets, shows RM cost); *Pending parts* action with reason; work-order detail lists parts with totals; clear "not enough stock" message. |
| Tests | **AC-03** (one `WorkOrderPart` + one `Issue` row + stock decreased, atomically — simulate a mid-way failure and assert nothing persisted); **AC-04** (insufficient stock → rejected, stock unchanged, no rows); **the concurrent-issue test** (N parallel requests for the last unit(s): exactly the available amount succeeds, stock never negative, reconciliation holds — real SQL Server, **run repeatedly**); return path; resume guard; access rows. |
| Ops & docs | README design decision: chosen concurrency strategy and why; test-plan AC-03/04. |

**Exit criteria**
- [ ] The concurrent-issue test is stable across repeated runs (this is the flagship test — do not merge it flaky).
- [ ] Stock reconciles with the ledger.

---

## Phase 8 — Customer signature

**Goal:** the first *file* through the whole stack, and the rule it enables. Introduces the storage abstraction with one small artifact.
**Demo:** on a phone, the customer signs on a canvas; the signature is stored and shown on the job; **Complete is refused until it exists**.
**Covers:** PRD §6.6 (signature part) · SDD §15 · BR-03 (complete) · **AC-06**. Requires **Open Question 2** (Azurite vs MinIO).

| Layer | Work |
|---|---|
| Data | Migration if the signature key/metadata column needs changes (reviewed). |
| Service | `IFileStorage` (`SaveAsync`, `OpenReadAsync`, `DeleteAsync`) with the chosen emulator implementation; type/size limits and unique object names; DB stores metadata + internal key, never a public URL. Completion now **requires a real stored signature** (BR-03) — the Phase 4 completion test is updated. |
| Route | `POST /api/work-orders/{id}/signature` (PNG; assigned technician; editable states only); **authorized** download endpoint with role/ownership checks. |
| UI | **Hand-rolled signature pad** (canvas, touch + mouse, clear/redo, exports PNG); technician step **Signature → Complete**; signature preview in the office work-order detail. |
| Tests | **AC-06** (complete rejected without signature, accepted with one); file limits and type validation; unauthorized download → 403/404; storage abstraction tested with a fake (unit) and the emulator (integration); access rows. |
| Ops & docs | **Storage emulator container added to `docker-compose.yml`** and CI; `IFileStorage` fake documented; seed jobs get signatures. |

**Exit criteria**
- [ ] Sign on a phone-sized viewport → complete → Manager sees the signature.
- [ ] Docker compose includes storage and the smoke job still passes.

---

## Phase 9 — Job photos

**Goal:** reuse the storage path built in Phase 8 for a second, richer artifact.
**Demo:** the technician takes or picks photos during the job; thumbnails show with progress and an option to remove before completion; the Manager sees a gallery on the order.
**Covers:** PRD §6.6 (photos) · SDD §15.

| Layer | Work |
|---|---|
| Data | `WorkOrderPhoto` rows (entity exists); migration only if a column is missing. Seed: jobs with photos. |
| Service | Photo upload: allowed types (jpeg/png/webp), max size (~8 MB), unique names, editable states only, remove-before-complete. |
| Route | `POST /api/work-orders/{id}/photos`, `DELETE /api/work-orders/{id}/photos/{photoId}`, authorized download. |
| UI | Technician step **Photos** (file input with `capture`, upload progress, error and retry, thumbnails); gallery in the office detail. |
| Tests | Type and size validation; ownership (another technician cannot upload or read); removal blocked after completion; access rows; UI test for the step. |
| Ops & docs | README storage section; seed photos are small fictional images (no personal data). |

**Exit criteria**
- [ ] A technician at 360 px uploads photos and completes the job; the Manager views them.

---

## Phase 10 — Service report PDF

**Goal:** the workflow's final artifact, produced from real, complete job data.
**Demo:** after completion the Manager generates a report with a unique number and downloads a correct PDF; after approval it can't be regenerated; the Reports page lists everything.
**Covers:** PRD §6.7 · SDD §15 · BR-14 (per D-05) · AC-11 rows. Requires **Open Question 3** (PDF library and its license).

| Layer | Work |
|---|---|
| Data | `ServiceReport` (entity exists): unique `ReportNumber` (e.g., `SR-2026-000123`); migration if a column/index is missing. Seed: at least one fully completed + approved job **with a report**. |
| Service | `IReportService`: builds a report data model from the completed work order (customer, equipment, problem, priority, diagnosis, work performed, parts with RM totals, photos as thumbnails, signature, technician, key timestamps in MYT), renders the PDF, stores it via `IFileStorage`. Regeneration only while `Completed` and not `Approved`; locked after approval (BR-14 per D-05). |
| Route | `POST /api/work-orders/{id}/generate-report` (Manager), `GET /api/work-orders/{id}/report` (office roles + assigned technician), `GET /api/reports`. |
| UI | Generate/view/download on the work-order detail; **Reports** page (list + download). |
| Tests | Report data-model correctness (assert on the model); a smoke check that a non-empty PDF is produced; unique report numbers under parallel generation; regeneration blocked after approval; access rows. |
| Ops & docs | Library recorded in README dependencies (D-06) with its license note; a sample report screenshot for the README. |

**Exit criteria**
- [ ] The whole demo runs in Docker: request → … → PDF report downloaded.

---

## Phase 11 — Manager dashboard (core metrics)

**Goal:** first real analytics through every layer. Start with the metrics that are cheap and demonstrable, and establish the named-operation pattern the AI will call later.
**Demo:** the Manager opens the dashboard and sees work orders by status, open requests by priority, jobs completed per month and the low-stock count — matching numbers a person can verify against the seed data.
**Covers:** PRD §6.9 (first half) · **AC-09 (dashboard)** · SDD §14. Requires **Open Question 6** (hand-rolled charts vs a library).

| Layer | Work |
|---|---|
| Data | Indexes justified by these queries (reviewed). |
| Service | `IAnalyticsService` with **named, parameterised operations**, each independently testable: `OpenRequestsByPriority`, `WorkOrdersByStatus`, `JobsCompletedPerMonth` (bucketed in **MYT**), `LowStockSummary`. Metric definitions (units, filters, treatment of cancelled/rejected) written in code comments and the README. |
| Route | `GET /api/dashboard/summary` (Manager/Admin). |
| UI | Replace the placeholder dashboard: KPI tiles, hand-rolled SVG/CSS bars (preferred, D-06), accessible labels, loading/empty/error states. |
| Tests | Each operation over a small **deterministic dataset** with known answers, incl. empty data and month boundaries in MYT; **AC-09** through the summary; a latency sanity check on the seed dataset (NFR-03, < 500 ms target); access rows. |
| Ops & docs | Metric definitions in the README; seed spans several months so charts are meaningful. |

**Exit criteria**
- [ ] Dashboard numbers equal hand-computed values on the seed data.

---

## Phase 12 — Analytics depth

**Goal:** finish the metric list and tailor the dashboard to each role. Same slice shape, more operations.
**Demo:** the Manager also sees average time request→assignment, average completion duration, repeated failures by equipment and top parts by usage; Warehouse sees stock; ServiceStaff sees their request queue.
**Covers:** PRD §6.9 (second half), §5 (role-appropriate dashboards).

| Layer | Work |
|---|---|
| Data | Additional indexes if profiling shows a need. |
| Service | New named operations: `AverageTimeToAssignment`, `AverageCompletionDuration`, `RepeatedFailuresByEquipment` (≥ N requests in a window), `TopPartsByUsage` (quantity/cost). Role-specific summary shaping. |
| Route | Extend `GET /api/dashboard/summary`; role-aware payloads. |
| UI | Additional tiles/charts; role-specific dashboards (Manager full, Warehouse stock, ServiceStaff queue). |
| Tests | Known-answer tests per operation including boundaries; role-shaped payload tests; access rows. |
| Ops & docs | README metric definitions completed. |

**Exit criteria**
- [ ] All PRD §6.9 metrics are computed by the backend and match the seed data.

---

## Phase 13 — AI Technician Assistant (mock-first)

**Goal:** the first AI feature through every layer, behind a provider-agnostic interface, with a deterministic mock so it works with no key and no cost. Requires **Open Question 1** (provider) only to add a real client — the mock lets work proceed.
**Demo:** on the Diagnosis step the technician taps *Get suggestions* and sees possible causes, suggested checks, relevant past cases and relevant parts, clearly labelled *AI-generated assistance — not a certified diagnosis*. Suggestions can be copied into the notes but never auto-applied.
**Covers:** PRD §6.10 (AI-01) · SDD §14 · NFR-09.

| Layer | Work |
|---|---|
| Data | None. Seed: enough past work orders on the demo equipment for "relevant past cases" to be meaningful. |
| Service | `IAiService` / `ILlmClient` (`CompleteStructuredAsync(prompt, schema, ct)`) with **`MockLlmClient`** (default in dev, test and Docker) — provider, model, timeout come from configuration, keys only from user-secrets/env. **Bounded context builder**: problem description + equipment record + that equipment's *permitted, capped* history (no data from unrelated customers). Output validated against a schema; mandatory disclaimer field. AI code paths get **read-only** services (no write access); output size limits; timeout with graceful "AI unavailable"; free text treated as data, never as instructions; simple per-user rate limit. |
| Route | `POST /api/ai/technician-assistant` (Technician, Manager). |
| UI | "Get suggestions" panel on the technician Diagnosis step and the office detail; disclaimer label; copy-to-notes; unavailable/failed states. |
| Tests (mock provider) | Schema-validation failure handling; provider timeout/exception → controlled error; a technician **cannot** pull another customer's history through the assistant; **AI endpoints never modify DB state** (assert counts/row versions unchanged); access rows. |
| Ops & docs | README "AI boundaries" section; **no key or real provider response in the repo**. |

**Exit criteria**
- [ ] Works end to end with the mock in Docker; switching to a real provider is configuration only.

---

## Phase 14 — AI Manager Insights

**Goal:** the second AI feature, built on Phase 11–12 analytics so the LLM only ever summarizes backend-computed numbers.
**Demo:** the Manager asks "how many jobs were completed last month?" and gets a summary with the **underlying numbers shown beside it**; an unsupported question gets a controlled refusal listing what can be asked.
**Covers:** PRD §6.10 (AI-02) · **AC-10**.

| Layer | Work |
|---|---|
| Data | None. |
| Service | Question → **intent mapper** to an **allow-list** of existing analytics operations (rule/keyword-based first; optional LLM classification restricted to the allow-list). Execute the operations, send **only the resulting numbers** to the LLM for a natural-language summary; the LLM never sees raw tables and never computes metrics. Unsupported/ambiguous → refusal listing supported topics. Same read-only, timeout and rate-limit rules as Phase 13. |
| Route | `POST /api/ai/manager-insights` (Manager). |
| UI | Manager AI page: question box, example questions, answer with numbers beside the summary, refusal messages. |
| Tests (mock provider) | **AC-10:** every number in the answer equals the analytics output (assert the payload sent to the client); unsupported question → refusal; schema/timeout handling; AI never mutates state; access rows. |
| Ops & docs | Supported question list in the README; test-plan AC-10. |

**Exit criteria**
- [ ] Both assistants work with the mock; **AC-10** passes; cut-line: at least 3–4 supported questions plus the refusal path.

---

## Phase 15 — Release polish

**Goal:** things that genuinely cannot be sliced into earlier phases. This is a short verification-and-presentation phase, **not** the first time tests, security or docs are considered.
**Covers:** PRD §7 (Q-01…Q-06), §9, §11 · D-07.

- [ ] **AC traceability verification:** every AC-01…AC-11 has a named passing test in `docs/test-plan.md` (the map has been growing since Phase 3; this only fills gaps).
- [ ] **One final security sweep** (`security-review` / `code-review`): IDOR checks (technician on others' jobs, file downloads), mass assignment, JWT lifetime/issuer/audience, CORS for non-dev, upload validation, logs without secrets, HTTPS behaviour, plus `dotnet list package --vulnerable`, `npm audit`, and a secret scan of the **full git history**.
- [ ] **Performance & accessibility sweep:** N+1 queries, `AsNoTracking`, pagination on list endpoints; keyboard navigation and contrast; 360 px technician view and ≥ 1024 px office view.
- [ ] **Seed dataset review:** ~10 customers, ~30 equipment, parts below minimum, records in **every lifecycle state**, one fully completed + approved job with photos, signature and report, one demo account per role.
- [ ] **Clean-machine test:** clone into a new folder or VM, run only the documented commands, walk the whole demo.
- [ ] **README, portfolio-grade:** pitch, screenshots/GIFs, **2–3 minute demo video**, architecture diagram, ERD, workflow diagrams, API examples, design decisions, dependencies and why, known limitations, V2 roadmap. Decide **Open Question 7** (SDD v1.2 vs PRD-as-amendment) and write `docs/decisions.md`.
- [ ] **Decide Open Question 4** (live deployment: try a free tier for at most a day, else skip).
- [ ] **Flip the repo public (MIT)** — only on the owner's explicit go-ahead; replace the CI badge URL; repo description/topics.

**Exit criteria — Definition of Done (PRD §11)**
- [ ] `docker compose up` yields a seeded, working system on a clean machine.
- [ ] CI green; README complete; demo video linked; repo public with a clean history.

---

## Phase 16 — Stretch (only after Phase 15)

Same tracer rule applies: thin end-to-end first.

- **16A — React Native tracer (P2, Open Question 5):** `src/SarawakBizOps.Mobile` (Expo + TypeScript). Slice 1: **login → My Jobs list** against the same API. Then thicken one step at a time (Job Details → Start → Diagnosis → Parts → Photos → Signature → Complete) — each step a small working slice. No offline sync (V2). Secure token storage; CI build check only.
- **16B — Live deployment (P2):** free/cheap host for API + DB + storage, HTTPS, env-based secrets, mock AI unless a key is budgeted, "demo data resets" note.
- **16C — V2 ideas (document only, do not build):** offline creation/sync, stock reservation, reopen/correction flow with an AuditLog, customer portal, email/SMS, real-time updates, refresh tokens, forced password change on first login.

---

## Cross-phase checklists

### Definition of Ready for each phase
- [ ] Demo script written (the click-through that proves the slice).
- [ ] Requirement(s), BR(s), AC(s) identified.
- [ ] Schema changes identified; migration reviewed before applying.
- [ ] Endpoint list and roles agreed (match SDD §12).
- [ ] The thinnest version of the slice is named — and what is *deferred* is written down.

### Definition of Done for each phase (every layer)
- [ ] **Data:** migration reviewed; seed rows added.
- [ ] **Service:** rule in a service/pure class with unit tests.
- [ ] **Route:** endpoints role-restricted; **authorization rows added to `EndpointAccessTests`**.
- [ ] **UI:** page/step works with loading, empty and error states; technician steps checked at 360 px.
- [ ] **Tests:** unit + integration (real SQL Server) + at least one UI test; concurrency-sensitive slices have a repeat-run test.
- [ ] **Ops & docs:** `docker compose up` still works; CI green; README and `docs/test-plan.md` updated.
- [ ] Demo script executed by hand; tag `phase-N-done`.

## Traceability: acceptance criteria → phase

Each AC is implemented **and** tested in the same phase (the old plan pushed AC-11 to a final "matrix" phase; now its rows accumulate).

| AC | Statement (short) | Phase (implemented + tested) |
|---|---|---|
| AC-01 | Assign creates exactly one WorkOrder, request marked assigned | 4 |
| AC-02 | Technician sees customer/equipment/problem/priority/status | 4 |
| AC-03 | Add part → WorkOrderPart + one Issue transaction, atomic | 7 |
| AC-04 | Insufficient stock rejected, stock unchanged | 7 |
| AC-05 | Completion rejected without diagnosis/work performed | 4 |
| AC-06 | Completion rejected without signature | 8 |
| AC-07 | Manager approval locks the work order | 4 (first cut), 5 (central guard) |
| AC-08 | Technician edit after approval rejected | 5 |
| AC-09 | Low-stock parts appear on dashboard | 6 (API), 11 (dashboard) |
| AC-10 | Manager AI uses backend-computed data only | 14 |
| AC-11 | Unauthorized role rejected regardless of UI | every phase (`EndpointAccessTests` rows), verified in 15 |

## Cut-lines if time runs short

Because every phase is a working slice, cutting never leaves the app broken. Drop in this order — the earlier items are the cheapest to lose:

1. Phase 16 (React Native, live deploy) — already stretch.
2. A real AI provider — keep the mock and the interface.
3. Phase 14 breadth — keep 3–4 supported questions plus the refusal path.
4. Phase 12 (analytics depth) — keep the Phase 11 tiles.
5. Phase 9 (photos) — the signature (Phase 8) is the rule-bearing evidence; photos are richer, not required by a rule. *Note this weakens PRD Goal 3; do it only under real time pressure.*
6. Frontend test breadth — keep login/role-route tests.

**Never cut:** atomic assignment (Phase 4), state-machine guards (Phases 4–5), atomic inventory + concurrency test (Phase 7), server-side authorization tests (every phase), Docker one-command run (Phase 2), README.

## Decisions to make, and when

| # | Open question (PRD §13) | Decide by | Default if undecided |
|---|---|---|---|
| 1 | AI provider | Start of Phase 13 (only needed for a real client) | Mock only |
| 2 | Azurite vs MinIO | Start of Phase 8 | Azurite (Azure Blob SDK) |
| 3 | PDF library / license check | Start of Phase 10 | Simplest option whose license fits a public MIT repo; record in README |
| 4 | Live deployment | Phase 15 | Skip |
| 5 | React Native | End of Phase 15 | Move to V2 |
| 6 | Dashboard charts approach | Start of Phase 11 | Hand-rolled SVG/CSS |
| 7 | SDD v1.2 vs PRD-as-amendment | Phase 15 | PRD-as-amendment + `docs/decisions.md` |
| 8 | Concurrency strategy for stock (RowVersion retry vs `UPDLOCK`) | Start of Phase 7 | Bounded `RowVersion` retry (matches the existing schema) |
| 9 | Conflict token transport (`If-Match` vs body field) | Start of Phase 5 | Body field `rowVersion` (simplest for a hand-rolled client) |
