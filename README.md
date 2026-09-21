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
| System Design Document v1.1 | Source of truth for data model, lifecycles, business rules (BR-01…14) and acceptance criteria (AC-01…11). Where the PRD and the SDD disagree, the PRD wins. |

## Status

| Plan phase | Scope | State |
|---|---|---|
| 0 | Foundation & hygiene (git, tests, CI, error shape) | Done |
| 1 | Users admin, change-password, Customers/Equipment completion | Done |
| 2 | Deployable skeleton: `docker compose up` with seed data | Built; awaiting first green CI run |
| 3–4 | Request intake, then the first job end to end (tracer bullet through every layer) | Planned |
| 5–10 | Workflow rules, parts and stock, signature, photos, PDF report | Planned |
| 11–14 | Dashboard and analytics, AI assistants | Planned |
| 15 | Release polish and docs | Planned |

**Built so far:** Identity with 5 seeded roles and a seeded admin, JWT login, change-password, Admin user
management (create with one role, edit, deactivate, reset password) with immediate token revocation, all 10
entities with the `InitialCreate` migration, Customers and Equipment (create/update, service history for office
roles), RFC 7807 error responses, Swagger with bearer auth, and a React office console (role-aware sidebar
and routes, Users, Customers, Equipment, detail pages with history, change password).

```
src/
├── SarawakBizOps.Api/          ASP.NET Core Web API + EF Core + Identity (+ Dockerfile)
└── SarawakBizOps.Web/          React + TypeScript office console (+ Dockerfile, nginx.conf)
tests/
└── SarawakBizOps.Api.Tests/    xUnit; integration tests run against real SQL Server (Testcontainers)
scripts/
└── compose-smoke.sh            Signs in through the web container and reads data (used by CI)
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

The sample data is fictional: 5 customers across Kuching, Sibu and Miri and 10 pieces of equipment.

| Command | What it does |
|---|---|
| `docker compose down` | Stop, keeping the database |
| `docker compose down -v` | Stop **and wipe the database**; the next `up` re-seeds from scratch |
| `docker compose logs -f api` | Follow the API log |

**How it fits together**

- Only the web container (nginx, port 8080) is published. It serves the built React app and proxies `/api` to the API container, so the browser sees one origin: no CORS and no base-URL setup. The API base URL (`/api`) is fixed when the web image is built.
- `Database__MigrateOnStartup=true` makes the API apply EF migrations at startup, retrying while SQL Server warms up. The flag is **off by default** everywhere else, so a normal run never changes a schema on its own.
- The API is healthy (`/health/ready`) only once migrations and seeding are done, because it does its startup work before it starts listening. `/health` is a plain liveness check. Both are unauthenticated and return no data.
- The seeding is idempotent: every row is inserted only if its key (email, company name, serial number) is missing, so restarting is safe.
- SQL Server is also published on `127.0.0.1:14330` (this machine only) if you want to connect with SSMS or Azure Data Studio: user `sa`, password `Demo_Passw0rd!`.

**Overriding the demo defaults.** Every secret in `docker-compose.yml` is a labelled, demo-only default so a fresh clone runs with no setup. To change any of them, copy [`.env.example`](./.env.example) to `.env` and uncomment the line: `WEB_PORT`, `DEMO_PASSWORD`, `JWT_KEY`, `MSSQL_SA_PASSWORD`, `SQL_HOST_PORT`. The database volume keeps the old `sa` password, so run `docker compose down -v` after changing `MSSQL_SA_PASSWORD`.

**Smoke test.** [`scripts/compose-smoke.sh`](./scripts/compose-smoke.sh) signs in as every role through the web container and checks the seeded data. It needs `curl` and `jq`, and CI runs it on every push.

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
```

The first run downloads the SQL Server image (about 1.5 GB) and takes a couple of minutes; later runs
take well under a minute. CI (GitHub Actions) builds the solution, runs the tests and builds the web app on every push and PR.

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

### Design decisions carried from the SDD

- **No custom password/role fields.** Identity owns all of that.
- **`RowVersion` concurrency tokens** on `WorkOrder` and `Part`, for transactional, concurrency-safe
  status changes and stock issuing (a conflicting save throws `DbUpdateConcurrencyException` instead
  of silently overwriting someone else's change).
- **Repository layer omitted** and **manual DTO mapping**: less indirection for an app this size.
- **`launchSettings.json` pins the dev ports** (`5080`/`5443`) so the web app's `.env.example` points at a real port.

---

## Known limitations

- The Docker demo accounts share one public, documented password and there is no forced change on first login (V2). They exist only in demo mode.
- The Docker setup serves plain HTTP; TLS is out of scope for the demo.
- Only Customers and Equipment are implemented so far; the workflow engine, inventory, files/PDF,
  dashboard analytics and AI features are planned in [`plan.md`](./plan.md).
- There is no forgot-password email flow (out of scope): an Admin resets a forgotten password, and there is
  no forced change-on-first-login. Admin-set passwords are shared out of band.

## License

[MIT](./LICENSE)
