# SarawakBizOps — Product Requirements Document (PRD)

| | |
|---|---|
| **Status** | Draft v1.0 — agreed via interview, 2026-09-21 |
| **Owner** | Winnie Ngu |
| **Baseline design** | *SarawakBizOps – System Design Document v1.1 (Reviewed)* — remains the source of truth for data model, lifecycles, business rules (BR-01…14), API list and acceptance criteria (AC-01…11). This PRD does **not** repeat them; it defines the destination, scope, deviations and definition of done. Where this PRD and the SDD disagree, **this PRD wins** and the SDD must be revised deliberately (SDD §24). |
| **Repo state at writing** | Phases 1–4 of the SDD roadmap done (see §4). |

---

## 1. Product summary

SarawakBizOps is a **fictional, Sarawak-based field-service management platform** for a small/medium industrial-equipment servicing company. It moves a job from a customer's problem report to an approved, documented, inventory-accounted service report:

```
Customer report → Service Request → Manager approval → Technician assignment → Work Order
→ Field execution (diagnosis, parts, photos, signature) → Completion → Service Report (PDF) → Manager approval → Analytics / AI
```

The backend is the system of record and enforces every rule; the clients (office web app, technician view) are thin. AI is downstream of the workflow: it advises and summarizes, and never changes business data.

## 2. Purpose and audience

**Primary purpose: a hiring portfolio piece.** The product is judged by recruiters/engineers who will read the repo, run it, and skim the README in a few minutes.

| Audience | What they need to conclude |
|---|---|
| Recruiter / hiring manager | "This person can ship a complete, realistic, full-stack system." (clear README, screenshots, demo video, one-command run) |
| Engineer reviewing code | "This person understands state machines, transactions, concurrency, authorization and testing." (clean layering, tests that prove the hard rules) |
| Fictional end users (for realism of the domain) | Office roles at a Sarawak field-service SME (Kuching / Sibu / Miri) and their technicians. |

Because it is a portfolio project, **depth in the hard parts (workflow integrity, inventory concurrency, server-side authorization, tests) beats breadth of features.**

## 3. Goals and non-goals

### Goals
1. A complete, working **Customer → Equipment → ServiceRequest → WorkOrder → Report** workflow, end to end, with role-based access.
2. **Provable correctness** of the hard rules: lifecycle guards, atomic inventory issue, no double-spend of stock, locked approved records.
3. Technician can **complete a job with evidence** (diagnosis, work performed, parts, photos, customer signature) from a phone-sized screen.
4. Manager gets **real dashboard metrics** computed by the backend, plus AI assistance grounded in those metrics.
5. **Reproducible by a stranger:** `docker compose up` produces a seeded, working system.

### Non-goals (unchanged from SDD §3)
Payroll · accounting/financial ledger · full CRM · procurement ERP · complex invoicing · live GPS · route optimization · IoT telemetry · AR repair · predictive-maintenance ML · customer-facing portal · **full offline sync** (V2).

### Additional non-goals decided in this PRD
- No multi-tenancy, no i18n (English only), no email/SMS notifications, no payment features.
- No AuditLog table and no "reopen completed job" flow (see §8, D-05).
- No public hosted production deployment as a requirement (see §9).

## 4. Current state (verified in the repo)

**Backend — `src/SarawakBizOps.Api` (ASP.NET Core 8, EF Core, SQL Server LocalDB, Identity + JWT)**
- Done: Identity with 5 seeded roles + seeded admin; JWT login and `GET /api/auth/me`; all 10 entities + enums + Fluent API configs + `InitialCreate` migration; `RowVersion` on `WorkOrder` and `Part`; Customers (list/get/create/update); Equipment (list-by-customer/get/create); exception-handling middleware; Swagger with bearer auth; CORS for Vite.
- Layering in place: `Controller → Service → ApplicationDbContext`, manual DTO mapping, no repository layer.
- **Missing vs SDD §12:** `change-password`, all `/api/users`, `PUT /equipment/{id}`, `*/history`, all service-request / work-order / parts / inventory / report / dashboard / AI endpoints, DTO validation, file storage, structured logging beyond defaults.
- **Empty:** `Services/` has only Auth, Customers, Equipment. No test project exists.

**Web — `src/SarawakBizOps.Web` (React 18 + TypeScript + Vite, react-router, plain CSS tokens, no UI/state libs)**
- Done: login, `AuthContext` (JWT in `localStorage`), protected routes, app shell, Dashboard (customer/equipment counts only), Customers list/create/edit, Equipment list/create with customer filter.
- **Missing:** everything in §6 beyond Customers/Equipment.

**Project hygiene**
- Not a git repo yet. `.gitignore` already excludes `bin/ obj/ node_modules/ .env.local`.
- `bin/`, `obj/` and `tsbuildinfo` files exist on disk and must stay untracked.
- README documents Phases 1–4 and dev setup.

## 5. Users and roles

Roles are ASP.NET Core Identity roles (SDD §5). Authorization is **always enforced server-side** (BR-08, AC-11); the UI only hides what the API would reject.

| Role | Surface | Can do (summary) |
|---|---|---|
| Admin | Web | Create/deactivate users, assign roles; also Customers/Equipment (as today) |
| Manager | Web | Approve/reject requests, assign technicians, monitor, approve completed work, dashboard, manager AI, reports |
| ServiceStaff | Web | Manage customers/equipment, create service requests |
| Technician | Web (responsive technician view) → later Mobile (stretch) | See only **their assigned** work orders; start, diagnose, add parts, photos, signature, complete; technician AI |
| WarehouseStaff | Web | Manage parts, receive/adjust stock, view history and low stock |

**Demo accounts:** the seed creates one account per role (documented in README) so a reviewer can walk the whole flow.

## 6. Scope and requirements

Priority: **P0** = required for "Done"; **P1** = required for "Done" but sequenced last; **P2** = stretch.

### 6.1 Identity & users — P0
- Login, `me`, **change password**.
- Admin user management: list, create (with role), edit/deactivate, reset role. Deactivated users cannot log in (`IsActive`).
- Only Users with the `Technician` role can be assigned to a WorkOrder (BR-11).
- Web: Users page (Admin only). Technicians page (Manager): list technicians with current open jobs.

### 6.2 Customers & Equipment — P0 (partly done)
- Finish: `PUT /api/equipment/{id}` and edit UI; `GET /customers/{id}/history` and `GET /equipment/{id}/history` (service requests/work orders over time) with a history view.
- Equipment status changes (Active / Inactive / UnderMaintenance / Retired) editable by Admin/ServiceStaff.

### 6.3 Service Requests — P0
- Create (ServiceStaff/Admin), validating **Equipment belongs to Customer** (BR-10).
- List with filters (status, priority, customer), detail view.
- Manager: **approve**, **reject** (with reason), and **assign technician** which **atomically** creates the WorkOrder and marks the request `Assigned` (BR-01, BR-12, BR-13, AC-01).
- Cancel where permitted (`New`/`Approved` → `Cancelled`).
- State machine per SDD §8.

### 6.4 Work Orders — P0
- Lifecycle and guards exactly per SDD §9 table (`New → Assigned → InProgress ⇄ PendingParts → Completed → Approved`, `Cancelled` from pre-approval states).
- Technician endpoints: `mine`, `start`, `diagnosis` (PATCH), `parts`, `photos`, `signature`, `complete`; Manager: `approve`, `cancel`.
- Completion requires diagnosis + work performed + customer signature (BR-03, AC-05/06). Approved work orders are locked (BR-06, AC-07/08). Ownership check: a technician may act only on their own job (BR-02).
- Optimistic concurrency via `RowVersion`; conflicts return HTTP 409 with a clear message.
- Web: Work Orders list (Manager/office), detail with **status timeline**, and the **responsive Technician view** (see §6.8).
- `PendingParts` records a reason/part request; resume requires stock availability.

### 6.5 Inventory — P0
- Parts CRUD (WarehouseStaff/Admin), unique `PartNumber`, decimal quantities, `MinimumStockLevel`, `IsActive`.
- Inventory transactions: Receive, Issue, Return, Adjustment (mandatory reason). **Immutable** (BR-09); corrections via new Adjustment.
- Adding a used part to a WorkOrder creates the `WorkOrderPart` + exactly one Issue transaction + `QuantityOnHand` update **in one DB transaction** (BR-04/05/13, AC-03/04). Price snapshot copied to `WorkOrderPart.UnitPrice`.
- **Concurrency-safe:** two technicians cannot consume the same last stock; proven by an automated concurrent-issue test.
- Low-stock endpoint and UI (AC-09). Transaction history with filters (part, type, work order, date).
- Currency shown as **RM**.

### 6.6 Photos, signature, files — P0
- Photos and the customer signature (drawn on a canvas in the client) are uploaded through the API and stored in **blob storage, not SQL** (SDD §15). DB keeps metadata and a reference.
- Storage sits behind an `IFileStorage` abstraction. Local dev/demo runs an **S3/Azure-compatible emulator in Docker (Azurite or MinIO — see Open Questions)**.
- Files are served through an authorized API download endpoint (no permanent public URLs).
- Basic limits: allowed image types, max size, unique filenames.

### 6.7 Service Reports (PDF) — P0
- Generated from the `Completed` work-order snapshot: customer, equipment, problem, diagnosis, work performed, parts (with RM cost), photos, signature, technician, dates, unique `ReportNumber` (BR-14 spirit: no silent changes after generation).
- `POST /work-orders/{id}/generate-report`, `GET /work-orders/{id}/report`; stored as a file; access restricted by role.
- Web: Reports page (list + download/view).
- Regeneration is restricted; after Manager approval the report version is locked. (No reopen/correction flow — D-05.)

### 6.8 Technician experience — P0 (responsive web) / P2 (React Native)
- **P0:** a mobile-first, responsive technician view inside the existing React app: *My Jobs → Job Details (customer/equipment context) → Start → Diagnosis → Work Performed → Parts → Photos → Signature → Complete → View Report.* Large tap targets, minimal typing, few steps (NFR-04).
- **P2 (stretch):** React Native + Expo + TypeScript app reusing the same API and covering the same flow (SDD Phase 8). Not required for "Done"; only started when everything P0/P1 is complete.
- Full offline sync remains V2. MVP provides retry-safe submissions where practical (e.g., idempotent completion/part-add).

### 6.9 Manager dashboard & analytics — P0
- `GET /api/dashboard/summary` computes real metrics **in the backend**: open requests by priority, work orders by status, jobs completed per month, average time request→assignment, average completion duration, repeated failures by equipment, parts usage, low-stock count.
- Web dashboard renders these (charts/tiles), role-appropriate.

### 6.10 AI features — P1 (in scope, provider decided later)
- **AI-01 Technician Assistant** (`POST /api/ai/technician-assistant`): input is the problem description + equipment context + permitted history; output is structured (possible causes, suggested checks, relevant past cases, relevant parts). UI labels it clearly as *AI-generated assistance, not a certified diagnosis*.
- **AI-02 Manager Insights** (`POST /api/ai/manager-insights`): backend maps the question to **approved analytics operations**, runs them, then sends only the resulting numbers to the LLM for summarization. The LLM never computes authoritative metrics. Unsupported questions return a controlled refusal (AC-10).
- Rules: backend-only integration; keys never committed (NFR-09); AI never mutates business state; schema-validated output; timeout/failure handling.
- **Provider-agnostic design** (`IAiService`) so the provider can be chosen late; a deterministic **mock provider** must exist so the app, tests and Docker demo work with no API key and no cost.

### 6.11 Cross-cutting
- DTOs only (never expose entities); server-side validation with consistent error shape; 401 vs 403 semantics; async + cancellation tokens; no stack traces to clients (SDD §13).
- Structured logging of important operations and business timestamps (NFR-08 — timestamps + structured logs only, see D-05).
- Typical read APIs target < 500 ms on a dev deployment (NFR-03).

## 7. Non-functional and quality requirements

| ID | Requirement |
|---|---|
| Q-01 | SDD NFR-01…09 apply. |
| Q-02 | Locale: English only; currency **RM**; store UTC, display **MYT (UTC+8)**. |
| Q-03 | Secrets (JWT key, AI key, storage keys, DB password) come from user-secrets/env vars; never committed. The seed admin default password must be documented as demo-only. |
| Q-04 | Accessible, responsive UI: office console usable at ≥ 1024 px; technician view usable at 360 px width. |
| Q-05 | Handle loading, empty and error states on every page. |
| Q-06 | Conflicts (409), validation (400/422), auth (401/403), not found (404) surfaced with human-readable UI messages. |

### Testing (built up in every phase; see `plan.md`)
- **Unit (xUnit):** work-order and service-request state transitions and guards; completion rules; inventory rules; role/ownership policies.
- **Integration:** API + EF Core + **real SQL Server** (e.g., Testcontainers), covering the full workflow *request → approve → assign → work → complete → approve* and the reports flow.
- **Authorization tests:** Technician cannot approve; ServiceStaff cannot change inventory; Warehouse cannot assign; API rejects regardless of UI (AC-11).
- **Concurrency test:** parallel part issues against the last unit of stock — exactly one succeeds; stock never negative.
- **AI tests:** schema validation, timeout/failure, refusal of unsupported analytics — run against the mock provider.
- **Frontend:** a small set of component/route tests (login, protected routes, role-gated actions). No Playwright E2E required.
- Every SDD acceptance criterion **AC-01…AC-11** maps to at least one automated test; the mapping is listed in the README or a test-plan doc.

## 8. Design decisions taken in this PRD

| ID | Decision | Effect on the SDD |
|---|---|---|
| D-01 | Technician flow ships first as a **responsive web view**; React Native is **P2 stretch**. | Changes SDD Phase 8 from required to stretch. |
| D-02 | AI is **in scope (P1)** behind a provider-agnostic interface with a **mock** provider; real provider **TBD**. | Loosens the SDD's "OpenAI or Azure OpenAI". |
| D-03 | File storage uses an **S3/Azure-compatible emulator in Docker** behind `IFileStorage`. | Concretizes SDD §15/§21. |
| D-04 | Currency RM; UTC storage, MYT display; English only. | New. |
| D-05 | **No AuditLog table and no reopen-for-correction flow.** NFR-08 = timestamps + structured logs. BR-14 is met by locking approved orders and restricting regeneration. | Narrows SDD NFR-08/BR-14. |
| D-06 | **Minimal dependencies:** keep the starter approach (manual mapping, `fetch` wrapper, Context, plain CSS). Add a library only when the requirement demands it (e.g., PDF generation, test tooling, storage SDK, canvas/charts if not hand-rollable). Every addition is recorded in the README. | Keeps starter conventions; supersedes the README's TanStack Query suggestion unless it becomes clearly necessary. |
| D-07 | Repository is **public on GitHub, MIT-licensed**. | New. |
| D-08 | `RowVersion` on `WorkOrder`/`Part` stays (already added beyond SDD §6). | Recorded deviation. |

## 9. Delivery: demo, Docker, CI/CD, documentation

- **Docker:** `docker compose up` starts SQL Server, the API, the Web app and the storage emulator; applies migrations; loads seed data. No LocalDB required for reviewers.
- **CI (GitHub Actions):** on every push/PR — restore, build, run backend tests, build web, run frontend tests.
- **Live deployment:** *optional bonus* — only if a free/cheap tier works. Not required for Done.
- **Seed data (rich fictional Sarawak scenario):** ~10 customers across Kuching/Sibu/Miri; ~30 equipment items; a realistic parts catalogue with some below minimum; service requests and work orders in **every lifecycle state**; at least one completed job with report; one demo account per role.
- **README (portfolio-grade):** what/why, screenshots or GIFs, short demo video, architecture diagram, ERD, workflow diagram, API examples, how to run (Docker + manual), test instructions, demo accounts, design decisions, known limitations, roadmap/V2.

## 10. Roadmap and milestones

Capacity: **20+ hrs/week**, 3+ months available. The SDD estimates 6–8 focused weeks; the remaining plan is about 245–310 hours (roughly 12–16 weeks at 20 hrs/week), with buffer for debugging, polish and documentation.

Delivery follows the **tracer-bullet method**: every phase is a thin, production-quality vertical slice through data, service, route, UI and tests, with a demo script and exit criteria. [`plan.md`](./plan.md) owns the detailed sequencing, estimates and status; this table is the summary and the two must agree.

| Phase | Deliverable (what a user can newly do) | Status |
|---|---|---|
| 0 | Walking skeleton: git, MIT, CI, integration tests, RFC 7807 errors; sign in, list customers and equipment | Done |
| 1 | Users and master data: Admin manages users and roles, change-password, equipment edit, customer/equipment history | Done |
| 2 | Deployable skeleton: `docker compose up` gives a seeded, working system; CI proves it | Done |
| 3 | Request intake: staff raise a service request, Manager approves or rejects it | Done |
| 4 | First job end to end: assign, technician starts and completes, Manager approves (**responsive technician view** starts here) | |
| 5 | Workflow rules for real: cancel, locks, ownership, conflicts, status timeline | |
| 6 | Parts and stock in: warehouse manages the catalogue, receives stock, sees low stock | |
| 7 | Parts used on a job: atomic issue, pending parts, concurrency proof | |
| 8 | Customer signature: drawn on the phone, stored via `IFileStorage` and the emulator, required to complete | |
| 9 | Job photos: capture, upload, gallery | |
| 10 | Service report PDF: generate, lock, download, Reports page | |
| 11 | Manager dashboard (core metrics) | |
| 12 | Analytics depth: remaining metrics, role-specific dashboards | |
| 13 | AI Technician Assistant (mock-first) | |
| 14 | AI Manager Insights, grounded in backend-computed analytics | |
| 15 | Release polish: final sweeps, docs, demo video, **public flip** on the owner's go-ahead | |
| 16 (P2) | Stretch: React Native technician app, live deployment | Only after 15 |

Working loop per SDD §24, applied to every phase: **one vertical slice at a time**, each mapped to a requirement, business rule and acceptance criterion; review migrations before applying; run app and tests after each slice.

## 11. Definition of Done

The project is "done" when **all** of the following are true (SDD §25, adjusted):

- [ ] Authentication and role-based authorization work on the API and are covered by tests.
- [ ] Customer → Equipment → ServiceRequest → WorkOrder → Report works end to end in the web app.
- [ ] A Technician can complete a job — diagnosis, parts, photos, signature — from the responsive technician view.
- [ ] Every inventory change is traceable via immutable transactions; concurrent-issue test passes.
- [ ] Service report PDF is generated with correct work-order data and downloadable by authorized roles.
- [ ] Manager dashboard shows real database metrics.
- [ ] AI Technician Assistant and Manager Insights work, grounded in controlled context / backend-computed analytics (mock provider at minimum; real provider if chosen).
- [ ] AC-01…AC-11 each have at least one passing automated test.
- [ ] `docker compose up` gives a seeded, working system on a clean machine.
- [ ] GitHub Actions builds and tests on every push.
- [ ] Public MIT repo; no secrets in history; README complete with architecture, ERD, API examples, screenshots/video, known limitations.
- *(Stretch)* Live deployed demo · React Native technician app.

## 12. Risks

| Risk | Mitigation |
|---|---|
| Scope creep from the big SDD | Priorities in §6; RN is P2; AI behind mock; no reopen/audit flows. |
| Inventory concurrency bugs | Transaction + `RowVersion` design up front; dedicated concurrent-issue test in Phase 7. |
| Blob/file handling complexity in Docker | `IFileStorage` abstraction; emulator from day one of Phase 8. |
| AI provider cost/keys leaking | Mock provider default; keys via env only; no key in the demo. |
| Starter never compiled against real packages (README note) | Run `npm install`, `npm run build`, `dotnet build` in Phase 0 before new work (done). |
| Secrets exposure on a public repo | Verify `.gitignore`, scan history before first push; demo-only seed passwords. |

## 13. Open questions

1. **AI provider:** Anthropic Claude, OpenAI/Azure OpenAI, or mock only for the public demo? (Decision due before Phase 13.)
2. **Storage emulator:** Azurite (Azure Blob SDK) or MinIO (S3 SDK)? Both fit behind `IFileStorage`.
3. **PDF library:** needs one new dependency (D-06). Candidate: QuestPDF (check its license terms for a public MIT repo) — decide in Phase 10.
4. **Live deployment:** try a free tier in Phase 16, or skip?
5. **React Native:** confirm at Phase 15 whether time remains, or move to V2.
6. **Charts on the dashboard:** hand-rolled SVG/CSS vs. adding a chart library (D-06).
7. **SDD updates:** issue a v1.2 of the SDD to reflect D-01…D-08, or keep this PRD as the amendment record?
