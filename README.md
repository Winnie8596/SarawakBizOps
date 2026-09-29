# SarawakBizOps

A **fictional, Sarawak-based field-service management platform** for a small/medium
industrial-equipment servicing company. It takes a job from a customer's problem report to an
approved, documented, inventory-accounted service report:

```
Customer report → Service Request → Manager approval → Technician assignment → Work Order
→ Field execution (diagnosis, parts, photos, signature) → Completion → Service Report (PDF)
→ Manager approval → Analytics / AI
```

**Stack:** ASP.NET Core 8 · EF Core · SQL Server · ASP.NET Identity + JWT · React 18 + TypeScript (Vite)

> **Work in progress.** This is a portfolio project being built one vertical slice at a time.
> The backend is the system of record and enforces every business rule; the web clients are thin.

[![CI](../../actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)

## Documents

| Document | Purpose |
|---|---|
| [`prd.md`](./prd.md) | Product requirements: scope, priorities, design decisions (D-01…D-08), Definition of Done |
| [`plan.md`](./plan.md) | Tracer-bullet plan: each phase is a vertical slice (data, service, route, UI, tests) with a demo script and exit criteria, plus AC traceability |
| [`docs/test-plan.md`](./docs/test-plan.md) | Acceptance-criteria map (AC-01…AC-11 → the tests that prove them), grown phase by phase |
| System Design Document v1.1 | Source of truth for data model, lifecycles, business rules (BR-01…14) and acceptance criteria (AC-01…11). Where the PRD and the SDD disagree, the PRD wins. |

## Status

| Plan phase | Scope | State |
|---|---|---|
| 0 | Foundation & hygiene (git, tests, CI, error shape) | Done |
| 1 | Users admin, change-password, Customers/Equipment completion | Done |
| 2 | Deployable skeleton: `docker compose up` with seed data | Done |
| 3 | Request intake: staff raise a service request, a Manager approves or rejects it | Done |
| 4 | The first job end to end: assign, technician works it, Manager approves | Done |
| 5 | Workflow rules for real: cancellation, a central lock guard, RowVersion conflicts, status timeline | Done |
| 6 | Parts and stock in: the warehouse runs the parts catalogue, receives and adjusts stock, sees low stock and the ledger | Done |
| 7 | Parts used on a job: atomic, concurrency-safe stock issue; PendingParts and resume | Done |
| 8–10 | Customer signature, photos, PDF report | Planned |
| 11–14 | Dashboard and analytics, AI assistants | Planned |
| 15 | Release polish and docs | Planned |

**Built so far:** Identity with 5 seeded roles and a seeded admin, JWT login, change-password, Admin user
management (create with one role, edit, deactivate, reset password) with immediate token revocation, all 10
entities with the `InitialCreate` migration, Customers and Equipment (create/update, service history for office
roles), **service requests** (raise, list with filters, approve, reject with a reason, cancel with a reason),
**work orders** (atomic assignment to a technician, start, diagnosis, complete, Manager approval, cancel with a
reason), a **central lock guard** so a locked (Approved/Cancelled) record refuses every mutating call, a
**RowVersion conflict token** required on every mutating call (409 "record changed — refresh" on a stale one),
a **parts catalogue and immutable inventory ledger** (receive, adjust with a reason, low stock, filtered history;
stock always equals the sum of its ledger rows), **parts used on a job** (atomic issue and return, PendingParts),
RFC 7807 error responses, Swagger with bearer auth, and a React office console (role-aware sidebar and routes,
Users, Customers, Equipment, Service Requests, Work Orders, Inventory, detail pages with history, status timeline, a shared
conflict banner, change password) plus a separate mobile-first **Technician view** (`/tech`) for the field
workflow.

### Service requests (Phase 3, cancellation added in Phase 5)

```
                     ┌──approve──► Approved ──assign──► Assigned
New ─────────────────┤
 │                    └──cancel───► Cancelled ◄──cancel──┘ (Manager only, once Approved)
 └────reject───► Rejected

Cancel from New: ServiceStaff, Admin or Manager (the creator-side roles undo their own mistake).
Cancel from Approved: Manager only.
```

| Who | Can |
|---|---|
| Admin, Manager, ServiceStaff | list and open requests (`GET /api/service-requests`, filters `status`, `priority`, `customerId`) |
| ServiceStaff, Admin | raise one (`POST /api/service-requests`) |
| Manager | approve (`POST /{id}/approve`), reject (`POST /{id}/reject`), cancel a New or Approved request (`POST /{id}/cancel`) |
| ServiceStaff, Admin | cancel their own New request (`POST /{id}/cancel`) |
| Technician, WarehouseStaff | nothing here (a Technician sees only their own work orders, below) |

- **BR-10:** the equipment must belong to the chosen customer, otherwise `400`. Retired equipment is also refused; Inactive and Under Maintenance equipment can be raised against.
- **Invalid moves are `409`.** Approving, rejecting or cancelling anything that is not in the right state returns a message such as *"This request is Approved and cannot be rejected. Only a New request can be approved or rejected."*, and the UI shows it.
- **Two Managers, one request.** Every mutating call (`approve`/`reject`/`cancel`/`assign`) sends back the record's `rowVersion`; the write is a single conditional `UPDATE … WHERE Status = @from AND RowVersion = @rowVersion`. If two decisions race — or a status change races an unrelated edit — exactly one changes a row and the other gets a `409` with "This record was changed by someone else. Refresh and try again.", so a decision is never silently overwritten.
- The creator always comes from the sign-in token, never from the request body.

**Try it (demo script).** After `docker compose up`, sign in at <http://localhost:8080>:

1. As `staff@sarawakbizops.local`: **Service Requests → New service request**. Pick *Rajang Timber Mills*, and note the equipment list contains only that customer's equipment. Describe the fault, set a priority, submit. You get a *Request #N submitted* notice.
2. As `manager@sarawakbizops.local`: **Service Requests** opens on the **New** requests. Open the one you raised (or the Urgent boiler request), and **Approve** it. Status flips to *Approved* at once and the buttons disappear.
3. Open another New request and choose **Reject**. The *Confirm rejection* button stays disabled until you type a reason, and the reason is then shown on the request.
4. Try to approve an already-approved request from a second browser tab: the page shows the 409 message and refreshes to the real state.
5. Open **Customers → Rajang Timber Mills → View & history**: the requests appear with their status, dates in Malaysia time (UTC+8), and link to their detail page.
6. As `warehouse@…` the Service Requests page is not offered and `/service-requests` bounces to the dashboard; as `technician@…` it bounces to **My Jobs** instead (see below).

### Work orders (Phase 4, cancellation and the lock guard added in Phase 5, PendingParts in Phase 7)

```
Approved ──assign──► Assigned ──start──► InProgress ──complete──► Completed ──approve──► Approved (locked)
 (request)            (atomic:         │   ▲    │  (technician)    (needs a                (Manager;
                       BR-01/11/12/13) │   │    │                   diagnosis and            read-only after)
                                       │ resume pending-parts       work performed, AC-05)
                                       │ (stock │
                                       │  back) ▼
                                       │  PendingParts
                        New ──────────►┤     │
                                       └─────┴──cancel──► Cancelled (locked; Manager only)
                                                  │
                                                  └─► the linked request reverts to Approved,
                                                       so it can be reassigned (BR-12's index
                                                       now excludes Cancelled rows)
```

| Who | Can |
|---|---|
| Manager | assign an Approved request to an active Technician (`POST /api/service-requests/{id}/assign`), approve a Completed job (`POST /api/work-orders/{id}/approve`), cancel a New/Assigned/InProgress/PendingParts job (`POST /{id}/cancel`) |
| Admin, Manager, ServiceStaff | list and open any work order (`GET /api/work-orders`, `GET /{id}`), including its parts and RM total |
| Technician | see only their own jobs (`GET /api/work-orders/mine`), start (`POST /{id}/start`), save diagnosis and work performed (`PATCH /{id}/diagnosis`), add and return parts, pause for parts and resume (see *Parts used on a job* below), complete (`POST /{id}/complete`) |
| WarehouseStaff | nothing here |

- **BR-01/11/12/13, AC-01:** assignment is one atomic step — the request must be `Approved`, the technician must be active with the Technician role, and a request can produce at most one **live** work order (a filtered unique index — excluding `Cancelled` rows — is the final guard). A second assign attempt, or two Managers racing on the same request, always leaves exactly one work order; cancelling a job frees its request to be assigned again.
- **BR-02, AC-02:** a technician acts only on their own job. Another technician's job is reported **404**, not 403, on every technician endpoint — its existence is not confirmed to someone who cannot open it. Office roles can read any job.
- **AC-05:** completion is rejected without a stored diagnosis and work performed. The technician UI has one button ("Complete job") that saves both fields and completes in the same tap, even though the API models them as two calls.
- **AC-07/AC-08, the central lock guard:** once a job is `Approved` or `Cancelled`, *every* mutating call refuses it with a `409` naming the lock — not just the status-changing ones. The diagnosis PATCH used to check this with its own inline `if`; it now goes through the same `WorkOrderStateMachine.IsLocked` check as everything else.
- **RowVersion conflicts:** every mutating call (`start`/`diagnosis`/`complete`/`approve`/`cancel`) sends back the job's `rowVersion`. This is what actually protects the diagnosis PATCH, which does not change status: two technicians (or two tabs) editing the same in-progress job both match `WHERE Status = 'InProgress'`, but only the one whose `RowVersion` still matches wins — the other gets the same "record changed — refresh" `409`.
- The Technician role gets its own **mobile-first view at `/tech`** (no office sidebar, 360 px layout, 44 px+ tap targets, a sticky primary action button) — chosen automatically the moment a Technician signs in or lands on `/`.

**Try it (demo script).** Continuing from the Service Requests walk above:

1. As `manager@sarawakbizops.local`: open **Service Requests**, filter to **Approved** and open the Centrifugal Pump request (the one seeded request left unassigned on purpose). Choose **Assign to a technician**, pick `Ahmad Bujang` (shows his current open-job count) and confirm. You land on a link to the new work order.
2. Sign in as `technician@sarawakbizops.local` on a narrow browser window (or your phone): you land on **My Jobs**, not the office dashboard. Open the pump job, tap **Start job**, type a diagnosis and what you did, then tap **Complete job** — one tap saves both fields and completes it.
3. Back as the Manager, open **Work Orders**, find the job you just completed and tap **Approve**. It becomes read-only from then on.
4. Try completing an in-progress job (there is a seeded one, the Air Compressor) with an empty diagnosis: the Complete button stays disabled until both fields are filled in, matching what the API would reject.
5. As `staff@sarawakbizops.local`, open the assigned pump request: no Assign button is offered, and a hint points at the Work Orders page instead.

**Cancellation and conflicts (Phase 5).**

1. As `staff@sarawakbizops.local`, raise a request, then open it and choose **Cancel request…** — type a reason and confirm. It moves straight to *Cancelled*; a Manager could have cancelled it too, but not another technician or warehouse account.
2. As `manager@sarawakbizops.local`, open the seeded **Assigned** job (Band Saw) and choose **Cancel job…**. Once confirmed, its request shows *Approved* again — open **Service Requests** and you can assign it to a technician a second time.
3. Open a work order's detail page: the **Timeline** panel lists every dated step (raised/assigned/started/completed/approved/cancelled) derived purely from its own timestamp columns — there is no separate history table.
4. Open the same work order in two browser tabs as the Manager, approve it in one, then click Approve in the other: the second tab shows the shared conflict banner ("This record was changed by someone else…") and reloads to the real state — the same banner every page uses for a `409`.

### Parts and stock (Phase 6)

```
Receive     (+q, warehouse)          ┐   one database transaction:
Adjustment  (±q, reason required)    ├─► 1. UPDATE Parts SET QuantityOnHand = QuantityOnHand + q
                                     │      WHERE Id = @id AND QuantityOnHand + q >= 0
Issue/Return  only from work-order   ┘   2. INSERT exactly one immutable InventoryTransaction
              parts (Phase 7, below)
Invariant: QuantityOnHand == SUM(ledger quantities) for every part, and never below zero.
```

| Who | Can |
|---|---|
| WarehouseStaff, Admin | create and edit parts (`POST /api/parts`, `PUT /api/parts/{id}`), receive and adjust stock (`POST /api/inventory-transactions`) |
| WarehouseStaff, Admin, Manager | list/search parts (`GET /api/parts?search=&includeInactive=`), open one (`GET /{id}`), see low stock (`GET /api/parts/low-stock`), read the ledger (`GET /api/inventory-transactions`, filters `partId`, `type`, `workOrderId`, `from`, `to`) |
| Technician | search the **active** parts list only (`GET /api/parts`; `includeInactive` is ignored), to pick parts for their job — no low stock, single part or ledger |
| ServiceStaff | nothing here |

- **BR-09, the ledger is the truth.** `QuantityOnHand` is a cached balance that only the inventory service moves, always in the same database transaction as exactly one new ledger row. There is **no PUT, PATCH or DELETE** for ledger rows (a test enumerates the app's routes to prove it); a mistake is fixed with an offsetting Adjustment. Editing a part never touches its stock, and a new part starts at zero — its first delivery is a Receive.
- **Adjustments need a reason** (`400` without one) and can go either way; a Receive must be positive. Issue and Return are refused here (`400`): they only ever come from adding or returning parts on a work order.
- **Never below zero, even under concurrency.** The stock change is a single conditional `UPDATE … SET QuantityOnHand = QuantityOnHand + @q WHERE QuantityOnHand + @q >= 0`, so the database computes the balance from its current value and a movement that would go negative matches no row (`409` "Not enough stock: only N on hand"). Eight simultaneous write-offs racing for three units leave exactly three winners. A `CHECK (QuantityOnHand >= 0)` constraint is the database backstop.
- **Low stock (AC-09, API half):** active parts at or below their `MinimumStockLevel`, most urgent first. Inactive parts never appear, and cannot receive stock (their leftovers can still be written off). The Phase 11 dashboard will show the same list.
- Quantities are decimals (up to 3 places, for litres and metres); money is RM with 2 places. A value with more places is refused rather than silently rounded. Part edits carry the part's `rowVersion` like every other edit; a stock movement bumps it, so an edit made against a stale screen gets the usual `409`.
- The history's date filter takes UTC instants (`from` inclusive, `to` exclusive); the web app turns whole Malaysia-time days into that window.

**Try it (demo script).** Sign in at <http://localhost:8080>:

1. As `warehouse@sarawakbizops.local`: the sidebar shows **Inventory**. The list has 11 active parts with RM prices; four rows are highlighted **Low stock** (the conveyor belt is at 0 m, 10 below its minimum). Tick **Low stock only** to see just those four.
2. **New part** — e.g. `BRG-6206-2RS`, *Ball bearing 6206*, unit `pcs`, RM 21.90, minimum 10. It is saved with no stock and shows as low.
3. On that row choose **Receive**, enter `25` and a delivery note, and confirm: *Received 25 pcs… On hand now 25 pcs*, and the row is no longer low.
4. Choose **Adjust** on the same part, pick *Remove stock*, enter `2`: **Confirm adjustment** stays disabled until you type a reason. Then try removing `100`: the API refuses it — *Not enough stock: only 23 pcs on hand* — and nothing changes.
5. Open **History** on that row (or **Inventory → transaction history**): the receive and the adjustment, newest first, signed, with who did them and why. Filter by type or by a date range in Malaysia time. There is nothing to edit or delete.
6. As `manager@…` the Inventory pages are read-only (no New part, Receive, Adjust or Edit). As `staff@…` they are not offered and `/inventory` bounces to the dashboard; a technician lands on **My Jobs**.

### Parts used on a job (Phase 7)

```
POST /api/work-orders/{id}/parts  { partId, quantity, rowVersion }      — no price: it is snapshotted server-side
one database transaction:
  1. claim the job:  UPDATE WorkOrders SET Status = Status
                     WHERE Id = @id AND TechnicianId = @me AND Status = 'InProgress' AND RowVersion = @rv
  2. issue stock:    UPDATE Parts SET QuantityOnHand = QuantityOnHand - @q
                     WHERE Id = @part AND IsActive = 1 AND QuantityOnHand - @q >= 0     (0 rows → 409, AC-04)
  3. INSERT WorkOrderPart (UnitPrice = Part.UnitCost read under the row lock)
     INSERT exactly one Issue ledger row (-q, WorkOrderId = @id)                         (AC-03, BR-05)
DELETE /api/work-orders/{id}/parts/{lineId}  { rowVersion }  — the same claim, then the line is removed,
  the stock goes back up and exactly one Return ledger row (+q, WorkOrderId) is written.
```

| Who | Can |
|---|---|
| Technician (their own job, InProgress only) | add a part (`POST /api/work-orders/{id}/parts`), return a whole line (`DELETE /{id}/parts/{lineId}`), pause the job for parts (`POST /{id}/pending-parts` with a reason, the part and the quantity needed) |
| Technician (their own job, PendingParts) | resume it (`POST /{id}/resume`) once the awaited part has the awaited quantity on hand |
| Office roles | see the parts, RM line totals and job total on the work-order page, and the job's Issue/Return rows in the ledger (`?workOrderId=`) |

- **AC-03, atomic.** The line, exactly one Issue row and the stock decrease commit together or not at all. A test makes the database fail part-way (before the rows are written, and after they are written but before commit) and checks that stock, lines, ledger and even the job's `rowVersion` are exactly as before.
- **AC-04, BR-04.** Asking for more than is on hand is a `409` — *"Not enough stock: only 2 pcs of … on hand"* — and nothing changes.
- **Concurrency-safe (Open Question 8).** Issuing uses the same single conditional `UPDATE` as the warehouse: the database serialises everyone on the part's row and re-checks `QuantityOnHand - @q >= 0`, so N technicians racing for the last units get exactly as many successes as there is stock, never a negative balance, with no retry loop. The flagship test runs that race in five rounds (e.g. 8 technicians for 3 units, 8 for 2.5 units in 0.5s) and reconciles the ledger after each.
- **The job's `rowVersion` guards the job, not the stock.** Adding or returning a part carries the job's `rowVersion` (the Phase 5 rule) and bumps it. The claim holds the job row's lock until commit, so a Manager's cancel or the technician's own Complete cannot interleave, and a double-tap from the same screen adds the part once.
- **Price snapshot.** `UnitPrice` is copied from the catalogue at the moment of use; the client cannot send one, and a later price change never rewrites an existing line. Line totals are rounded to sen, and the job total is the sum of its lines.
- **Return** removes a whole line (to use fewer, return it and add the right amount). Parts can be added and returned only while the job is **InProgress**; an Approved or Cancelled job refuses with the central lock message. A job cancelled with parts on it keeps them: the ledger already shows them issued to that job, and the warehouse books any unused units back with an Adjustment.
- **PendingParts.** The technician names the part and quantity they are waiting for and why. That is refused when the part is already in stock (add it instead). **Resume is guarded by stock:** the status change and the stock check are one conditional `UPDATE`, so it succeeds only when the awaited part has at least the awaited quantity on hand. Nothing is reserved (V2): the stock can still be used on another job before this one adds it. `start` and `resume` are separate actions — `start` cannot be used to skip the stock guard.

**Try it (demo script).** Sign in at <http://localhost:8080>:

1. As `technician@sarawakbizops.local` (a narrow window works best): open the in-progress **Air Compressor** job. **Parts used** already shows the overload relay (1 × RM 145.00). Tap **Add a part**, type `filter`, tap the *Air filter element*, use **+** to make it 2 and tap **Add to job**: the line and the new RM total appear.
2. Tap **Add a part** again, pick the *Automatic voltage regulator* (1 in stock) and press **+** to ask for 2: the picker says *Not enough stock: only 1 pcs on hand* and offers **Wait for this part…** instead of Add. (The API refuses the same thing with a `409`, and stock is unchanged.)
3. **Return** the air filter line: it disappears and the filters go back into stock.
4. Open the **Bottle Filling Line B** job — it is **Waiting for parts**: 6 m of conveyor belt, none in stock. Tap **Resume job**: it is refused with *Still waiting for parts: 6 m … needed, 0 on hand*.
5. As `warehouse@…`, **Inventory → Conveyor belt, 600 mm → Receive** 10 m. Back as the technician, **Resume job** now works; add the 6 m of belt and complete the job.
6. As `manager@…`, open any of these work orders: the parts table shows each line and the RM total, and the timeline shows *Waiting for parts* and *Resumed*. **Inventory → transaction history**, filtered by type *Issue*, shows each part used, linked to its job.

```
src/
├── SarawakBizOps.Api/          ASP.NET Core Web API + EF Core + Identity (+ Dockerfile)
└── SarawakBizOps.Web/          React + TypeScript office console (+ Dockerfile, nginx.conf, Vitest tests in src/test)
tests/
└── SarawakBizOps.Api.Tests/    xUnit; integration tests run against real SQL Server (Testcontainers)
docs/
└── test-plan.md                Acceptance-criteria map: which test proves which AC
scripts/
└── compose-smoke.sh            Signs in through the web container, reads data, walks request intake, the first job end to end, parts and stock, and parts on a job (used by CI)
docker-compose.yml              sqlserver + api + web, one command
```

---

## Run with Docker (recommended)

The only prerequisite is [Docker](https://www.docker.com/products/docker-desktop/). No .NET SDK, Node or SQL Server install.

```bash
docker compose up --build
```

The first run pulls the SQL Server image (about 1.5 GB) and builds the API and web images, which takes a few minutes; after that it starts in under a minute. When it settles, open **<http://localhost:8080>**.

The API creates the schema and the sample data on first start. Sign in with any of these **demo-only** accounts; they all share the password `Demo!2026`:

| Role | Email |
|---|---|
| Admin | `admin@sarawakbizops.local` |
| Manager | `manager@sarawakbizops.local` |
| ServiceStaff | `staff@sarawakbizops.local` |
| Technician | `technician@sarawakbizops.local` |
| WarehouseStaff | `warehouse@sarawakbizops.local` |

> **These credentials are public and exist only for the Docker demo.** They are created only when `Seed__Demo=true`
> (set by `docker-compose.yml` and nothing else). The API refuses to start outside that mode unless you set
> `Seed:AdminPassword` yourself, so there is no built-in admin password on a real deployment.

The sample data is fictional: 5 customers across Kuching, Sibu and Miri, 11 pieces of equipment and 12 service requests (3 New, one of them Urgent; 2 Approved — one deliberately left unassigned, one Approved again because its job was cancelled; 5 Assigned; 1 Rejected with a reason; 1 Cancelled by its own creator while still New) spread over the last three weeks, plus 6 work orders — one in each of Assigned, InProgress (with its diagnosis already recorded and a relay used), PendingParts (waiting for 6 m of conveyor belt that is out of stock), Completed (awaiting Manager approval), Approved (the whole workflow closed, read-only, with its parts and RM total) and Cancelled (its request reverted to Approved, ready to be reassigned). The warehouse has a 12-part catalogue (11 active, one discontinued) whose stock comes from 19 ledger rows — deliveries, two reasoned adjustments and four parts issued to jobs — so every part reconciles with its ledger; four active parts are at or below their minimum stock level.

| Command | What it does |
|---|---|
| `docker compose down` | Stop, keeping the database |
| `docker compose down -v` | Stop **and wipe the database and the stored files**; the next `up` re-seeds from scratch |
| `docker compose logs -f api` | Follow the API log |

**How it fits together**

- Only the web container (nginx, port 8080) is published. It serves the built React app and proxies `/api` to the API container, so the browser sees one origin: no CORS and no base-URL setup. The API base URL (`/api`) is fixed when the web image is built.
- `Database__MigrateOnStartup=true` makes the API apply EF migrations at startup, retrying while SQL Server warms up. The flag is **off by default** everywhere else, so a normal run never changes a schema on its own.
- The API is healthy (`/health/ready`) only once migrations and seeding are done, because it does its startup work before it starts listening. `/health` is a plain liveness check. Both are unauthenticated and return no data.
- The seeding is idempotent: every row is inserted only if its key (email, company name, serial number) is missing, so restarting is safe.
- SQL Server is also published on `127.0.0.1:14330` (this machine only) if you want to connect with SSMS or Azure Data Studio: user `sa`, password `Demo_Passw0rd!`.

**Overriding the demo defaults.** Every secret in `docker-compose.yml` is a labelled, demo-only default so a fresh clone runs with no setup. To change any of them, copy [`.env.example`](./.env.example) to `.env` and uncomment the line: `WEB_PORT`, `DEMO_PASSWORD`, `JWT_KEY`, `MSSQL_SA_PASSWORD`, `SQL_HOST_PORT`. The database volume keeps the old `sa` password, so run `docker compose down -v` after changing `MSSQL_SA_PASSWORD`.

**Smoke test.** [`scripts/compose-smoke.sh`](./scripts/compose-smoke.sh) signs in as every role through the web container, checks the seeded data, walks request intake (raise, approve, reject, wrong roles refused, invalid move is a 409), and walks a first job end to end (assign, wrong role refused, second assign is a 409, start, complete-without-evidence is a 400, diagnose, complete, approve, then the approved job locks against further technician action), sending each record's `rowVersion` as the web app does, and walks the warehouse (low stock is seeded, ServiceStaff and Technician are refused, create a part, duplicate number is a 409, receive, adjustment without a reason is a 400, adjust with one, going below zero is a 409, Issue is refused, the ledger sums to the stock), and walks parts on a job (a fresh job for the demo technician: the technician can search parts but not read the ledger, the warehouse cannot add parts to a job, add a part at the snapshotted price, over-stock is a 409 and changes nothing, return it, one Issue and one Return on the job's ledger, pause for parts, resume refused while short, receive, resume). It needs `curl` and `jq`, and CI runs it on every push. It adds a few requests, a job and a part per run and consumes the seeded unassigned request, so `docker compose down -v` resets the demo data. It expects the seeded demo state (e.g. at least 3 New requests), so run it against a fresh volume.

---

## Manual development setup

Use this when you are changing code and want hot reload; otherwise prefer Docker above.

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (a newer SDK also works; the projects target `net8.0`)
- SQL Server LocalDB (ships with Visual Studio) or any SQL Server instance
- [Node.js](https://nodejs.org/) 18+ and npm
- Docker Desktop, needed to run the integration tests (and for the Docker setup above)

### Backend

```bash
cd src/SarawakBizOps.Api

# Set the JWT signing key OUTSIDE the repo. The placeholder in appsettings.json is
# deliberately rejected at startup, so the API will not run until you do this.
dotnet user-secrets set "Jwt:Key" "a-long-random-string-at-least-32-characters"

# Optional: choose the seeded admin password instead of the Development default
dotnet user-secrets set "Seed:AdminPassword" "something-only-you-know"

# Create the database (the migration already exists in the repo)
dotnet ef database update      # if missing: dotnet tool install --global dotnet-ef

dotnet run
```

Swagger opens at `http://localhost:5080/swagger`. Log in via `POST /api/auth/login`, click
**Authorize** and paste `Bearer {token}`. Point `ConnectionStrings:DefaultConnection` at your own
SQL Server if you are not using the default LocalDB instance.

> **Development credentials.** In the `Development` environment (the `dotnet run` launch profiles) the seeded admin
> is `admin@sarawakbizops.local` with the password `ChangeMe123!`, taken from `appsettings.Development.json`, unless
> you override `Seed:AdminPassword`. In any other environment the API will not start without `Seed:AdminPassword`.

### Web app

```bash
cd src/SarawakBizOps.Web
npm install
cp .env.example .env.local     # adjust VITE_API_BASE_URL if the API runs elsewhere
npm run dev
```

Open `http://localhost:5173` and sign in with the development admin account above.

### Tests

```bash
dotnet test                    # requires Docker running; starts a throwaway SQL Server container

cd src/SarawakBizOps.Web
npm test                       # Vitest + React Testing Library; no server or database needed
```

The first `dotnet test` run downloads the SQL Server image (about 1.5 GB) and takes a couple of minutes; later runs
take a couple of minutes at most. CI (GitHub Actions) builds the solution, runs both test suites, builds the web app
and runs the Docker smoke test on every push and PR. Which test proves which acceptance criterion is listed in
[`docs/test-plan.md`](./docs/test-plan.md).

Every new endpoint gets rows in `EndpointAccessTests`, a table that calls each endpoint as an anonymous visitor and as every
role and checks the exact status (401, 403 or success), so a missing `[Authorize]` cannot go unnoticed.

---

## Architecture and conventions

- **Layering:** `Controller (thin) → Service (rules, transactions) → ApplicationDbContext`. DTOs only,
  manual mapping, no repository layer.
- **Authorization is server-side.** `[Authorize(Roles=…)]` plus ownership checks in services. The UI hides
  what the API would reject, as a courtesy and not as security.
- **Errors** are RFC 7807 `application/problem+json` for every 4xx/5xx (validation failures add an
  `errors` map; 500s carry a `traceId`, never a stack trace). The web `apiFetch` wrapper turns these into a
  single `ApiError`, and a 401 on an authenticated call ends the session and redirects to `/login`.
- **Sessions end immediately when they should.** Every JWT carries the Identity security stamp and is checked
  on each request, so deactivating a user, changing their role or changing their password revokes their
  existing tokens at once (one primary-key lookup per request).
- **Time and money:** UTC in storage, MYT (UTC+8) on display; currency shown as RM.

### Dependencies and why

Minimal by design (PRD D-06): a library is added only when a requirement demands it.

| Where | Package | Why |
|---|---|---|
| API | ASP.NET Core Identity, EF Core SQL Server, JwtBearer, Swashbuckle | Auth, persistence, API docs |
| Web | React, react-router-dom | UI and routing; plain CSS, `fetch` wrapper, Context (no UI or state library) |
| Tests | xUnit, Microsoft.AspNetCore.Mvc.Testing, Testcontainers.MsSql | Integration tests against the real API and real SQL Server, so transactions and `RowVersion` concurrency behave as in production |
| Web tests (dev only) | Vitest, jsdom, React Testing Library (`@testing-library/react`, `/dom`, `/jest-dom`, `/user-event`) | Role-gating, routing and form-flow tests of the real components (Phase 3, PRD D-06 test tooling). `fetch` is stubbed by hand, so there is no mocking library. |

> **Known advisory (dev tooling only).** `npm audit` reports Vitest 2 as critical: *"when the Vitest UI server is listening, arbitrary file can be read"*. We run `vitest run` headless in CI and never start the UI or API server, and none of these packages are in the Docker image or the built app. The fix needs Vite 6 or later, so it is left for the Phase 15 dependency sweep together with the existing Vite/esbuild dev-server findings.

### Design decisions carried from the SDD

- **No custom password/role fields.** Identity owns all of that.
- **Stock is moved by one conditional `UPDATE`, not read-modify-write** (Phase 6): `QuantityOnHand = QuantityOnHand + @q WHERE QuantityOnHand + @q >= 0`. The database serialises concurrent updates to the row and re-checks the predicate, so warehouse movements need no retry loop. **Phase 7 (Open Question 8) uses the same statement to issue parts to a job**, rather than the plan's original default of a bounded `RowVersion` retry or an explicit `UPDLOCK` read: the conditional `UPDATE` already takes the row's update lock and re-checks the predicate, so it is race-free in one round trip; a retry loop could still fail under heavy contention and an `UPDLOCK` read needs raw SQL for no gain. The price snapshot is read after the `UPDATE`, inside the same transaction, while the row is locked. The work order's own `RowVersion` still guards the *job* (see *Parts used on a job*).
- **`RowVersion` concurrency tokens** on `ServiceRequest`, `WorkOrder` and `Part`, checked by hand rather
  than via EF's `DbUpdateConcurrencyException`: every mutating call extends the same conditional
  `UPDATE … WHERE Status = @from` guard already used for status races with `AND RowVersion = @rowVersion`,
  so a status conflict and a same-status conflict (e.g. two edits to a diagnosis) both land on one
  consistent `409`, instead of a second EF-specific error path living alongside the first.
- **Repository layer omitted** and **manual DTO mapping**: less indirection for an app this size.
- **`launchSettings.json` pins the dev ports** (`5080`/`5443`) so the web app's `.env.example` points at a real port.

---

## Known limitations

- The Docker demo accounts share one public, documented password and there is no forced change on first login (V2). They exist only in demo mode.
- The Docker setup serves plain HTTP; TLS is out of scope for the demo.
- Customers, Equipment, Service Requests (raise, approve, reject, cancel) and the work-order lifecycle (assign,
  start, diagnose, complete, approve, cancel) are implemented so far, with a RowVersion conflict token on every
  mutating call and a shared conflict banner in the UI, plus the parts catalogue and inventory ledger (receive,
  adjust, low stock, history) and parts used on jobs (atomic issue and return, PendingParts/resume); files/PDF,
  dashboard analytics and AI features are planned in [`plan.md`](./plan.md). Photos and the signature are not part
  of completing a job yet (Phases 8–9), and low stock is an Inventory-page list until the Phase 11 dashboard.
  Stock is not reserved for a job waiting for parts (V2). A returned part goes back as a whole line. Cancellation history is kept only on the cancelled record itself (timestamp, actor, reason) — there is
  no separate audit-log table (D-05). List endpoints are not paginated yet (Phase 15 sweep).
- There is no forgot-password email flow (out of scope): an Admin resets a forgotten password, and there is
  no forced change-on-first-login. Admin-set passwords are shared out of band.

## License

[MIT](./LICENSE)
