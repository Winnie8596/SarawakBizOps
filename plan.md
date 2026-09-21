# SarawakBizOps — Multi-Phase Implementation Plan

| | |
|---|---|
| **Derived from** | [`prd.md`](./prd.md) (Draft v1.0, 2026-09-21) and *System Design Document v1.1 (Reviewed)* |
| **Capacity assumed** | 20+ hrs/week, ~12 weeks plus buffer |
| **Phase numbering** | Phases here are **plan phases (0–11)**, not the SDD's roadmap phases. Phase 0 here ≈ a hygiene pass before SDD Phase 5 resumes. |
| **Working loop** | Requirement → design decision → implement → run → test → inspect → fix → commit → document (SDD §24) |

## How to use this plan

1. Work **one phase at a time**, top to bottom. Each phase is a vertical slice: API + web + tests + docs for that slice.
2. A phase is finished only when its **Exit criteria** are all ticked. Do not start the next phase early (except where a task says "can overlap").
3. Every feature should trace to a **requirement (PRD §)**, **business rule (BR)** and **acceptance criterion (AC)**. The traceability table at the end tracks this.
4. **Tests are written inside each phase**, not saved for the end. Phase 9 is hardening and gap-filling, not first-time testing.
5. Commit at least once per task group; tag `phase-N-done` when exit criteria pass.
6. Review every EF migration before applying it (SDD §24).

### Conventions (apply in every phase)

- Backend layering: `Controller (thin) → Service (rules, transactions) → ApplicationDbContext`. DTOs only. Manual mapping. `CancellationToken` and async everywhere.
- Business rules live in services / small pure domain classes (e.g., state machines) so they are unit-testable without a database.
- Authorization: `[Authorize(Roles=…)]` **plus** ownership checks in services. The UI hides, the API enforces.
- Errors: one consistent JSON error shape; 400/422 validation, 401, 403, 404, 409 (concurrency/state conflict). No stack traces to clients.
- Frontend: follow `CustomersPage.tsx` shape (list + form + role-gated actions); each page has loading, empty and error states.
- New dependencies are added only when required (PRD D-06) and listed in the README "Dependencies & why" section.
- Store UTC; display MYT (UTC+8); money shown as RM.

### Effort and timeline overview

| Phase | Name | Est. hrs | Weeks | Priority |
|---|---|---|---|---|
| 0 | Foundation & hygiene | 15–20 | 1 | P0 |
| 1 | Identity, Users, Customers/Equipment completion | 25–30 | 1–2 | P0 |
| 2 | Service Requests | 20–25 | 2–3 | P0 |
| 3 | Work Order engine (API) | 35–45 | 3–5 | P0 |
| 4 | Inventory & atomic stock | 30–35 | 5–6 | P0 |
| 5 | Office web pages + responsive technician view | 40–50 | 6–8 | P0 |
| 6 | Files, signature, PDF service report | 25–30 | 8–9 | P0 |
| 7 | Manager dashboard & analytics | 20–25 | 9–10 | P0 |
| 8 | AI features (mock-first) | 25–30 | 10–11 | P1 |
| 9 | Hardening: tests, security, seed data | 25–30 | 11–12 | P0 |
| 10 | Docker, CI/CD, docs, demo | 25–30 | 12–13 | P0 |
| 11 | Stretch: React Native, live deploy | open | after 10 | P2 |

Total ≈ 285–355 hrs of P0/P1 work. At 20 hrs/week that is 14–18 weeks worst case, so the cut-lines at the end of this file matter.

---

## Phase 0 — Foundation & hygiene

**Goal:** a verified, version-controlled, buildable baseline with a test project and CI skeleton, before any new features.
**Covers:** PRD §4 (project hygiene), §9 (CI), §12 (starter never compiled against real packages), D-07.

### Tasks
- [ ] **Verify the baseline builds.** `dotnet restore && dotnet build` in `src/SarawakBizOps.Api`; `npm install && npm run build` in `src/SarawakBizOps.Web`. Fix any real-package typing/build errors (README warns they were never checked).
- [ ] **Run it once.** Apply `InitialCreate` to LocalDB, log in as the seeded admin via Swagger and the web app; confirm Customers/Equipment CRUD works.
- [ ] **Secrets audit.** Confirm `Jwt:Key` placeholder is unusable; `.env.local` ignored; no real secrets in `appsettings*.json`. Move any needed values to user-secrets.
- [ ] **Git.** `git init` at repo root; extend `.gitignore` (`*.tsbuildinfo`, `vite.config.js`/`.d.ts` generated files, `.vs/`, `TestResults/`, `coverage/`); verify `bin/ obj/` are untracked; first commit; create the public GitHub repo; add `LICENSE` (MIT).
- [ ] **Solution file.** Add `SarawakBizOps.sln` including the API and the new test project.
- [ ] **Test project.** `tests/SarawakBizOps.Api.Tests` (xUnit). Add a trivial passing test plus a smoke test using `WebApplicationFactory`. Decide the DB strategy now (see Phase 9 note: real SQL Server via Testcontainers for integration tests).
- [ ] **CI skeleton.** `.github/workflows/ci.yml`: restore, build, `dotnet test`, `npm ci`, `npm run build`. Green on `main`.
- [ ] **Error-shape and validation baseline.** Confirm `ExceptionHandlingMiddleware` returns a consistent shape; add model-validation (DataAnnotations) response shape to match. Add a 401-redirect-to-login handler in the web `client.ts`.
- [ ] **README cleanup.** Reflect real status, link to `prd.md` and `plan.md`, add demo-only warning for the seeded admin password.

### Exit criteria
- [ ] Clean clone → documented steps → API + web run and login works.
- [ ] CI is green on GitHub.
- [ ] Repo public with MIT license; secrets scan of history shows nothing.
- [ ] Test project runs (`dotnet test` passes).

---

## Phase 1 — Identity, Users, Customers/Equipment completion

**Goal:** finish the "foundation slice" so every later feature has real users of every role and complete master data.
**Covers:** PRD §6.1, §6.2 · BR-07, BR-08, BR-11 (groundwork) · AC-11 (start).

### Backend
- [ ] `POST /api/auth/change-password` (Identity `ChangePasswordAsync`).
- [ ] `IUserService` + `UsersController` (Admin only): `GET /api/users`, `POST /api/users` (create with role and initial password), `PATCH /api/users/{id}` (name, role, `IsActive`).
- [ ] Block login for `IsActive = false` users (in `AuthService`); reject tokens for deactivated users on `me`.
- [ ] Prevent an Admin from deactivating or demoting themselves / the last Admin.
- [ ] `PUT /api/equipment/{id}` (Admin, ServiceStaff); status changes allowed here.
- [ ] `GET /api/customers/{id}/history` and `GET /api/equipment/{id}/history` — return a stub-safe shape now (service requests / work orders arrive in Phases 2–3); wire the real data in those phases.
- [ ] DTO validation attributes (required, lengths, email, phone) on Customer/Equipment/User requests.

### Web
- [ ] **Users page** (Admin): list, create, edit role, deactivate/reactivate.
- [ ] **Change password** dialog/page for any logged-in user.
- [ ] Equipment **edit** form (reuse create form), status selector.
- [ ] Customer/Equipment **detail views** with a history section (empty for now).
- [ ] Role-aware sidebar: show only sections the role may use.

### Tests
- [ ] Unit/integration: only Admin can call `/api/users` (403 for others); deactivated user cannot log in; last-Admin protection; change-password success/failure.
- [ ] Equipment update validation (duplicate serial, missing customer).

### Exit criteria
- [ ] Admin can create one user per role from the UI and each can log in.
- [ ] Sidebar/UI differs by role; API rejects out-of-role calls with 403.
- [ ] Tests above pass in CI.

---

## Phase 2 — Service Requests

**Goal:** intake and manager review of requests, up to (but not including) assignment/WorkOrder creation.
**Covers:** PRD §6.3 · SDD §8 · BR-01, BR-10, BR-12 (groundwork) · FR: create/approve/reject.

### Backend
- [ ] `ServiceRequestStateMachine` (pure class): allowed transitions `New→Approved`, `New→Rejected`, `New/Approved→Cancelled`, `Approved→Assigned` (assigned transition is executed in Phase 3).
- [ ] `IServiceRequestService` + `ServiceRequestsController`:
  - `GET /api/service-requests` (filters: status, priority, customerId, equipmentId; paging basics)
  - `GET /api/service-requests/{id}`
  - `POST /api/service-requests` (ServiceStaff, Admin) — enforce **Equipment.CustomerId == CustomerId** (BR-10), set `CreatedByUserId` from the token.
  - `POST …/{id}/approve` and `…/reject` (Manager) — record `ApprovedByUserId`/`ApprovedAt`; reject requires a reason (add `RejectionReason` column via reviewed migration).
  - `POST …/{id}/cancel` (ServiceStaff/Manager/Admin) where permitted.
- [ ] Wire real request data into the customer/equipment `history` endpoints.
- [ ] Request DTOs (list item vs detail).

### Web
- [ ] **Service Requests page:** list with status/priority badges and filters; create form (customer picker → equipment picker filtered by that customer); detail drawer/page.
- [ ] Manager actions on detail: Approve / Reject (with reason) / Cancel, role-gated and state-aware (buttons only for valid transitions).
- [ ] Extend `StatusBadge` for request statuses and priorities.

### Tests
- [ ] State-machine unit tests: every valid and invalid transition.
- [ ] BR-10: equipment from another customer is rejected.
- [ ] Authorization: ServiceStaff cannot approve; Technician/Warehouse cannot create.

### Exit criteria
- [ ] ServiceStaff creates a request; Manager approves or rejects it; UI reflects state instantly.
- [ ] Invalid transitions return 409/400 with clear messages.

---

## Phase 3 — Work Order engine (API)

**Goal:** the core state machine — the heart of the portfolio. Everything except parts/inventory, photos and PDFs (which attach to it in later phases).
**Covers:** PRD §6.4 · SDD §9 · BR-01, 02, 03, 06, 11, 12, 13 · **AC-01, 02, 05, 06 (signature stubbed), 07, 08**.

### Backend
- [ ] `WorkOrderStateMachine` (pure): the full transition table from SDD §9 with actor role + guard predicates; unit-testable.
- [ ] **Assign (atomic):** `POST /api/service-requests/{id}/assign` (Manager) takes `technicianId`. In **one DB transaction**: verify request is `Approved`; verify user has `Technician` role and is active (BR-11); ensure no existing WorkOrder (BR-12, unique index); create WorkOrder (`Assigned`, `AssignedAt`); set request to `Assigned` (BR-13, AC-01).
- [ ] `IWorkOrderService` + `WorkOrdersController`:
  - `GET /api/work-orders` (Manager/office; filters status, technician, date)
  - `GET /api/work-orders/mine` (Technician — only own jobs)
  - `GET /api/work-orders/{id}` (office roles; Technician only if assigned — BR-02)
  - `POST …/start` (assigned technician; `Assigned→InProgress`, `StartedAt`)
  - `PATCH …/diagnosis` (assigned technician; editable states only — diagnosis, work performed, notes)
  - `POST …/pending-parts` and `…/resume` (technician; reason recorded; resume guarded by stock check once Phase 4 exists — leave a clear extension point)
  - `POST …/complete` (technician; requires diagnosis + work performed + **signature reference present** — BR-03, AC-05/06; signature upload arrives in Phase 6, so for now accept a stub flag the tests can set)
  - `POST …/approve` (Manager; `Completed→Approved`, `ApprovedAt`; lock — BR-06)
  - `POST …/cancel` (Manager; from `New/Assigned/InProgress`; reason)
- [ ] **Lock enforcement:** any mutating call on an `Approved`/`Cancelled` work order → 409 (AC-08). Centralize in one guard, not scattered `if`s.
- [ ] **Concurrency:** map `DbUpdateConcurrencyException` (RowVersion) to HTTP 409 with a "reload and retry" message. Return `RowVersion` in DTOs; accept it on state-changing calls (`If-Match` or body field — pick one and document).
- [ ] `GET /api/technicians` (Manager): technicians with open-job counts (feeds assign picker and the Technicians page).
- [ ] Structured log entries on every transition (who, what, from → to, timestamps) — NFR-08.
- [ ] Fill the work-order portion of the customer/equipment history endpoints.

### Tests (unit + integration)
- [ ] State-machine table tests for every transition, including forbidden ones.
- [ ] **AC-01:** assigning creates exactly one WorkOrder; second assign attempt fails (BR-12).
- [ ] **AC-02:** assigned technician can read job details; another technician gets 403/404.
- [ ] **AC-05/06:** completion rejected when diagnosis/work performed/signature is missing.
- [ ] **AC-07/08:** manager approval locks; technician edit after approval is rejected.
- [ ] Concurrency: two simultaneous status changes on one order → one succeeds, one gets 409.
- [ ] Authorization matrix for every endpoint above.

### Exit criteria
- [ ] Full workflow works via Swagger: request → approve → assign → start → diagnose → complete → approve.
- [ ] All listed AC tests pass in CI.

---

## Phase 4 — Inventory & atomic stock

**Goal:** trustworthy, immutable, concurrency-safe stock accounting.
**Covers:** PRD §6.5 · SDD §10, §18 · BR-04, 05, 09, 13 · **AC-03, 04, 09**.

### Backend
- [ ] `IPartService` + `PartsController`: `GET/POST /api/parts`, `GET/PUT /api/parts/{id}`, `GET /api/parts/low-stock` (`QuantityOnHand <= MinimumStockLevel`, active parts) — WarehouseStaff/Admin write; office read.
- [ ] `IInventoryService`:
  - `POST /api/inventory-transactions` (WarehouseStaff): `Receive` (+), `Adjustment` (signed, **mandatory reason**). `Issue`/`Return` are **not** creatable here — they come only from work-order part operations.
  - `GET /api/inventory-transactions` with filters (part, type, workOrderId, date range).
  - Immutability: no update/delete endpoints; DB-level safeguard where practical (e.g., no tracked updates in code; a test asserts none exist).
- [ ] **Add part to work order:** `POST /api/work-orders/{id}/parts` (assigned technician; editable states only). In **one transaction**: re-read part with concurrency protection, check `Quantity <= QuantityOnHand` (BR-04), create `WorkOrderPart` (price snapshot from `Part.UnitCost`), create exactly one `Issue` transaction (negative qty, linked to WO — BR-05), decrement `QuantityOnHand`. Never trust client price/quantity beyond the requested quantity.
- [ ] **Return / remove part:** technician can return a part while the WO is editable → `Return` transaction (+qty) and adjust/remove the `WorkOrderPart` (decide: reduce quantity vs. mark returned; keep transactions as the source of truth).
- [ ] **Concurrency strategy:** `Part.RowVersion` optimistic check with a bounded retry (e.g., 3 attempts) inside the transaction, or `UPDLOCK`/serializable read for the stock row. Choose one, document it in the README design decisions.
- [ ] `PendingParts → InProgress` resume guard: required stock available (extension point from Phase 3).
- [ ] Reconciliation helper (dev/test only): `QuantityOnHand == SUM(transaction quantities)` per part — used by tests to prove integrity.

### Tests
- [ ] **AC-03:** add part → one `WorkOrderPart` + one `Issue` transaction + stock decreased, atomically (simulate failure mid-way → nothing persisted).
- [ ] **AC-04:** insufficient stock → rejected, stock unchanged, no rows created.
- [ ] **AC-09:** low-stock endpoint includes parts at/below minimum.
- [ ] **Concurrent-issue test:** N parallel requests for the last unit(s) → exactly the available amount succeeds; stock never negative; reconciliation holds. Runs against real SQL Server.
- [ ] Immutability: no code path edits or deletes a transaction; Adjustment requires reason.
- [ ] Authorization: ServiceStaff cannot change inventory; Technician cannot Receive/Adjust; Warehouse cannot assign jobs.

### Exit criteria
- [ ] Stock always reconciles with the transaction ledger after the test suite.
- [ ] Concurrent-issue test is stable (run it repeatedly, not once).

---

## Phase 5 — Office web pages + responsive technician view

**Goal:** a complete, usable UI over Phases 1–4.
**Covers:** PRD §6.3–6.5 (web), §6.8 (P0 part), §6.1 (Technicians page) · NFR-04, Q-04, Q-05.

### Office console
- [ ] **Work Orders**: list with filters (status, technician); detail page with **status timeline**, customer/equipment context, diagnosis/work performed, parts used with RM totals, and Manager actions (assign from an approved request, approve completed, cancel with reason).
- [ ] **Assign flow:** from an approved service request → pick technician (with open-job count) → confirm; show resulting work order.
- [ ] **Inventory**: parts list (search, low-stock highlighting), part form, receive/adjust stock dialogs (reason required), transaction history view with filters.
- [ ] **Technicians** page (Manager): technicians and their open/completed jobs.
- [ ] Handle **409 conflicts** in the UI: show "This record changed — refresh" with a reload action.
- [ ] Shared UI bits: table, modal/drawer, toast/inline message, form field, pagination — as small internal components (no UI library, PRD D-06).

### Technician view (mobile-first, in the same React app)
- [ ] Separate route group `/tech/*`, chosen automatically for the `Technician` role after login.
- [ ] **My Jobs** (cards, priority + status, tap to open).
- [ ] **Job Details** with customer/equipment context and service history hint.
- [ ] Guided steps: **Start → Diagnosis → Work Performed → Parts (search + quantity stepper) → Photos (placeholder until Phase 6) → Signature (placeholder) → Complete**. Large tap targets, min 44 px, minimal typing, sticky primary action, works at 360 px width.
- [ ] Draft autosave in component state/`localStorage` so an accidental reload doesn't lose typed diagnosis (per-viewer convenience only).
- [ ] Retry-safe submits: disable button while pending; treat a repeated "complete"/"add part" as idempotent server-side where practical.

### Tests
- [ ] Component/route tests: protected routes, role-based redirects (Technician lands on `/tech`), role-gated buttons.
- [ ] Manual check at 360 px and ≥ 1024 px; keyboard focus works in forms and modals.

### Exit criteria
- [ ] All roles can perform their part of the workflow entirely from the UI (photo/signature stubs acceptable until Phase 6).
- [ ] No page lacks loading/empty/error states.

---

## Phase 6 — Files, signature, PDF service report

**Goal:** real evidence capture and the report artifact.
**Covers:** PRD §6.6, §6.7 · SDD §15 · BR-03 (real signature), BR-14 · AC-06 (real).

### Backend
- [ ] `IFileStorage` abstraction (`SaveAsync`, `OpenReadAsync`, `DeleteAsync`) with one implementation for the storage emulator chosen in **Open Question 2** (Azurite/Azure Blob SDK or MinIO/S3 SDK). Add the emulator container to a `docker-compose.dev.yml` now.
- [ ] `POST /api/work-orders/{id}/photos` (multipart; assigned technician; editable states): allowed types (jpeg/png/webp), max size (e.g., 8 MB), unique object name, `WorkOrderPhoto` row (URL is an internal key, not a public link).
- [ ] `POST /api/work-orders/{id}/signature` (PNG data/blob; assigned technician; sets `CustomerSignatureUrl`); allowed only in editable states.
- [ ] Authorized file download endpoints (e.g., `GET /api/files/{…}` with role/ownership checks) — no permanent public URLs.
- [ ] `complete` now requires a **real** stored signature (remove the Phase 3 stub).
- [ ] **PDF report** (`IReportService`): choose library per **Open Question 3** (record it in the README, D-06). Content: report number, customer, equipment, problem, priority, diagnosis, work performed, parts with quantities/RM cost/total, photos (thumbnail size), signature, technician, key timestamps in MYT.
- [ ] `POST /api/work-orders/{id}/generate-report` (Manager; requires `Completed` or `Approved`): unique `ReportNumber` (e.g., `SR-2026-000123`), store PDF via `IFileStorage`, create `ServiceReport` row; regeneration only allowed while `Completed` and not `Approved` (BR-14 interpretation, PRD D-05).
- [ ] `GET /api/work-orders/{id}/report` (office roles + assigned technician) streams the PDF; `GET /api/reports` list for the Reports page.

### Web
- [ ] Technician view: **camera/photo upload** (file input with `capture`), thumbnails, remove-before-complete, upload progress and error handling.
- [ ] **Signature pad** (canvas, touch + mouse, clear/redo, exports PNG) — hand-rolled, no library.
- [ ] Work order detail: photo gallery + signature preview.
- [ ] **Reports page:** list, view/download, generate button for Managers.

### Tests
- [ ] File limits and type validation; unauthorized download → 403/404.
- [ ] **AC-06:** completion rejected without signature; accepted with one.
- [ ] Report contains correct work-order data (assert on generated data model and a smoke check that a non-empty PDF is produced); unique report numbers; regeneration blocked after approval.
- [ ] Storage abstraction tested with a fake in unit tests and the emulator in integration tests.

### Exit criteria
- [ ] A technician on a phone-sized viewport completes a job with photos and a drawn signature; Manager generates and downloads a correct PDF.

---

## Phase 7 — Manager dashboard & analytics

**Goal:** real backend-computed metrics — also the "approved analytics operations" that Manager AI will call in Phase 8.
**Covers:** PRD §6.9 · AC-09 · SDD §14 (metric list).

### Backend
- [ ] `IAnalyticsService` with **named, parameterised operations** (each independently testable): open requests by priority; work orders by status; jobs completed per month; average time request→assignment; average completion duration (start→complete); repeated failures by equipment (≥ N requests in a window); parts usage (top parts by quantity/cost); low-stock count and list.
- [ ] `GET /api/dashboard/summary` composing the above (Manager/Admin; lighter version for other roles if needed).
- [ ] Define each metric precisely in code comments and the README (units, filters, treatment of cancelled/rejected items, time zone bucketing for "per month" in MYT).
- [ ] Add DB indexes justified by these queries (review migration).

### Web
- [ ] Replace placeholder dashboard with: KPI tiles, work orders by status, requests by priority, jobs per month, low-stock list, recent activity.
- [ ] Charts: hand-rolled SVG/CSS bars (preferred, D-06) or one small chart library (Open Question 6). Accessible labels and empty states.
- [ ] Role-appropriate dashboards (Manager full; Warehouse sees stock; ServiceStaff sees request queue).

### Tests
- [ ] Each analytics operation has tests over a small deterministic dataset (known answers), including boundary cases (empty data, month boundaries in MYT).
- [ ] **AC-09** via the dashboard summary.
- [ ] NFR-03: seed a realistic dataset and check summary latency is reasonable (< 500 ms target on dev).

### Exit criteria
- [ ] Dashboard shows numbers that match hand-computed values on the seed dataset.

---

## Phase 8 — AI features (mock-first)

**Goal:** grounded, safe AI assistance behind a provider-agnostic interface.
**Covers:** PRD §6.10 · SDD §14 · NFR-09 · **AC-10**. Requires **Open Question 1** (provider) decided by the start of this phase; the mock lets work proceed without it.

### Backend
- [ ] `IAiService` (or `ILlmClient`) abstraction: `CompleteStructuredAsync(prompt, schema, ct)`; implementations: **`MockLlmClient`** (deterministic, default in dev/test/Docker) and the chosen real provider client. Provider/key/model/timeout come from configuration; keys only via user-secrets/env vars.
- [ ] **AI-01 Technician Assistant** `POST /api/ai/technician-assistant` (Technician, Manager): builds a **bounded context** from the problem description, equipment record, and permitted history (that equipment's past work orders — capped, no data from unrelated customers). Output validated against a schema: possible causes, suggested checks, relevant past cases (IDs the caller may view), relevant parts. Response includes a mandatory disclaimer field.
- [ ] **AI-02 Manager Insights** `POST /api/ai/manager-insights` (Manager): question → **intent mapper** to an allow-list of Phase 7 analytics operations (rule/keyword-based first; optionally LLM-assisted classification restricted to the allow-list) → execute operations → send **only the resulting numbers** to the LLM for a natural-language summary. Unsupported/ambiguous question → controlled refusal listing supported topics. The LLM never sees raw tables and never computes metrics.
- [ ] Safety: AI code paths have **no write access** (inject read-only services); output size limits; timeout (e.g., 20 s) and graceful failure ("AI unavailable" — the rest of the app is unaffected); prompt-injection hygiene (treat free text as data, never as instructions to change behavior); log request metadata, not secrets.
- [ ] Simple per-user rate limit to protect cost.

### Web
- [ ] Technician view: "Get suggestions" panel on the diagnosis step; clear **"AI-generated assistance — not a certified diagnosis"** label; suggestions are copy-to-notes, never auto-applied.
- [ ] Manager AI page: question box, example questions, answer with the **underlying numbers shown beside the summary**, refusal messages.

### Tests (against the mock provider)
- [ ] **AC-10:** every number in a Manager AI answer comes from the analytics result (assert numbers in payload sent to the client equal analytics output).
- [ ] Schema validation failure handling; provider timeout/exception → controlled error; unsupported question → refusal.
- [ ] Technician cannot pull another customer's history through the assistant.
- [ ] AI endpoints never modify DB state (assert row versions/counts unchanged).

### Exit criteria
- [ ] Both assistants work end-to-end with the mock; switching to a real provider is configuration-only.
- [ ] No API key or provider response fixture contains sensitive data in the repo.

---

## Phase 9 — Hardening: tests, security, seed data

**Goal:** close gaps, prove the acceptance criteria, and prepare a convincing demo dataset.
**Covers:** PRD §7 (testing), §9 (seed), Q-01…Q-06, NFR-01…09 · **AC-01…AC-11 complete**.

### Tasks
- [ ] **AC traceability pass:** every AC-01…AC-11 has a named automated test; list them in `docs/test-plan.md` (or the README). Fill any gaps.
- [ ] **Authorization matrix test:** table-driven test that hits every endpoint with every role and asserts 200/403/401 as designed (AC-11).
- [ ] **Security review** (use the `security-review` / `code-review` skills): IDOR checks (technician on others' jobs, file downloads), mass-assignment (DTOs), JWT config (lifetime, issuer/audience, key length), CORS in non-dev, rate limiting on login (Identity lockout already on), file upload validation, logging without secrets, HTTPS redirect behavior.
- [ ] **Error handling pass:** consistent error shape everywhere; no stack traces; friendly UI messages for 400/401/403/404/409/500.
- [ ] **Performance pass:** N+1 queries, `AsNoTracking`, pagination on list endpoints, indexes reviewed against slow queries.
- [ ] **Seed data (rich Sarawak scenario):** ~10 customers (Kuching/Sibu/Miri), ~30 equipment, a parts catalogue with several below minimum, requests/work orders in **every lifecycle state**, at least one fully completed + approved job with photos/signature/report, demo accounts per role. Seeding is idempotent and enabled via configuration (on in Docker demo; explicit in dev).
- [ ] **Frontend tests:** the small set from PRD §7 (login, protected/role routes, role-gated actions, one form validation flow).
- [ ] **Logging:** structured logs on key operations verified; correlation/request id in logs if cheap.
- [ ] **Accessibility & responsive sweep:** keyboard nav, contrast, 360 px technician view, ≥ 1024 px office view.
- [ ] **Dependency & secret scan:** `dotnet list package --vulnerable`, `npm audit`, secret scan of full git history.

### Exit criteria
- [ ] Test suite green in CI, including the concurrency test and authorization matrix.
- [ ] Fresh database + seed produces a demo that exercises every screen.

---

## Phase 10 — Docker, CI/CD, docs, demo

**Goal:** a stranger can run it in one command and understand it in five minutes.
**Covers:** PRD §9, §11 · D-03, D-07.

### Tasks
- [ ] **Dockerfiles:** multi-stage for API (`dotnet publish`) and Web (build → static serve via nginx or similar).
- [ ] **`docker-compose.yml`:** services — `sqlserver`, `api`, `web`, storage emulator (+ bucket/container init). Health checks; API waits for DB; **migrations applied on startup** in demo mode; seeding on; env-driven config; volumes for DB and storage. A `.env.example` documents variables; no real secrets committed.
- [ ] **Web config for containers:** API base URL configurable at build/runtime; CORS origin set for the compose network/host port.
- [ ] **CI/CD (GitHub Actions):** build + backend tests (with SQL Server service/Testcontainers) + web build/tests on every push/PR; cache dependencies; optionally publish Docker images to GHCR on tag; status badge in README.
- [ ] **README (portfolio-grade):** pitch and problem statement; screenshots/GIFs; **demo video** (2–3 min walkthrough of request → approved report, plus AI); architecture diagram; ERD; workflow/state diagrams (request + work order); API examples (curl/Swagger); how to run (Docker first, manual second); demo accounts (marked demo-only); test instructions; design decisions (Identity, atomic assignment, inventory ledger, RowVersion, AI boundaries); dependency list with reasons; known limitations; V2 roadmap.
- [ ] **Docs folder:** `docs/architecture.md`, `docs/test-plan.md` (AC map), optional `docs/decisions.md` (ADR-style log of PRD D-01…D-08 and later decisions). Decide **Open Question 7** (SDD v1.2 vs PRD as amendment record).
- [ ] **Clean-machine test:** clone into a new folder/VM, run only documented commands, confirm the demo works.
- [ ] **Optional live deployment** (Open Question 4): only if a free/cheap tier works within a day; otherwise skip and note in README.
- [ ] Final polish: repo description/topics on GitHub, pinned on profile, LICENSE, CONTRIBUTING not needed.

### Exit criteria — Definition of Done (PRD §11)
- [ ] `docker compose up` yields a seeded, working system on a clean machine.
- [ ] CI green; README complete; demo video linked; repo public with MIT license and clean history.

---

## Phase 11 — Stretch (only after Phase 10 is done)

### 11A — React Native technician app (P2, Open Question 5)
- [ ] `src/SarawakBizOps.Mobile` (Expo + TypeScript); share API types with the web app (copy or small shared types folder).
- [ ] Screens per SDD §16: Login → My Jobs → Job Details → Start → Diagnosis → Work Performed → Parts → Photos (camera) → Signature → Complete → View Report.
- [ ] Secure token storage (SecureStore), retry-safe submits; **no** offline sync (V2).
- [ ] Manual test on a device/emulator; screenshots and short clip in README; CI build check only.

### 11B — Live deployment (P2)
- [ ] Free/cheap host for API + DB + storage; env-based secrets; HTTPS; mock AI unless a key is budgeted; README link and "demo data resets" note.

### 11C — V2 ideas (document only, do not build)
- Offline creation/sync, stock reservation model, reopen/correction flow with an AuditLog, customer portal, email/SMS notifications, real-time updates.

---

## Cross-phase checklists

### Definition of Ready for each phase
- [ ] Requirement(s), BR(s), AC(s) identified.
- [ ] Schema changes identified; migration reviewed before applying.
- [ ] Endpoint list + roles agreed (match SDD §12).

### Definition of Done for each phase
- [ ] API + web + tests + docs updated for the slice.
- [ ] `dotnet test` and web build/tests green locally and in CI.
- [ ] README/docs updated; tag `phase-N-done`.

## Traceability: acceptance criteria → phase

| AC | Statement (short) | Phase implemented | Phase fully tested |
|---|---|---|---|
| AC-01 | Assign creates exactly one WorkOrder, request marked assigned | 3 | 3 |
| AC-02 | Technician sees customer/equipment/problem/priority/status | 3 (API), 5 (UI) | 3, 5 |
| AC-03 | Add part → WorkOrderPart + one Issue transaction, atomic | 4 | 4 |
| AC-04 | Insufficient stock rejected, stock unchanged | 4 | 4 |
| AC-05 | Completion rejected without diagnosis/work performed | 3 | 3 |
| AC-06 | Completion rejected without signature | 3 (stub) → 6 (real) | 6 |
| AC-07 | Manager approval locks the work order | 3 | 3 |
| AC-08 | Technician edit after approval rejected | 3 | 3 |
| AC-09 | Low-stock parts appear on dashboard | 4 (API), 7 (dashboard) | 7 |
| AC-10 | Manager AI uses backend-computed data only | 8 | 8 |
| AC-11 | Unauthorized role rejected regardless of UI | every phase | 9 (matrix) |

## Cut-lines if time runs short

Drop in this order — the earlier items are the cheapest to lose without weakening the portfolio story:

1. Phase 11 (React Native, live deploy) — already stretch.
2. Real AI provider — keep the mock and interface (Phase 8 still ships).
3. Manager AI (AI-02) intent breadth — keep 3–4 supported questions plus the refusal path.
4. Dashboard chart polish — keep KPI tiles and simple bars.
5. Technicians page — fold its data into the assign dialog.
6. Frontend test breadth — keep only the login/role-route tests.

**Never cut:** atomic assignment, state-machine guards, atomic inventory + concurrency test, server-side authorization tests, Docker one-command run, README.

## Decisions to make, and when

| # | Open question (PRD §13) | Decide by | Default if undecided |
|---|---|---|---|
| 1 | AI provider | Start of Phase 8 | Mock only |
| 2 | Azurite vs MinIO | Start of Phase 6 | Azurite (Azure Blob SDK) |
| 3 | PDF library / license check | Start of Phase 6 | Pick the simplest MIT/community-licensed option; record in README |
| 4 | Live deployment | Phase 10 | Skip |
| 5 | React Native | End of Phase 10 | Move to V2 |
| 6 | Dashboard charts approach | Start of Phase 7 | Hand-rolled SVG/CSS |
| 7 | SDD v1.2 vs PRD-as-amendment | Phase 10 | PRD-as-amendment + `docs/decisions.md` |
