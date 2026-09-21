# SarawakBizOps — Full-Stack Starter (Phases 1–4)

This is the working foundation of the SarawakBizOps portfolio project, generated directly from
**System Design Document v1.1**: a backend API plus a matching React web app, covering one
complete vertical slice (Auth → Customers → Equipment) end-to-end. It is not the whole system —
see "What's deliberately not included yet" below for what's next, on purpose.

Two projects, meant to run side by side:

```
src/
├── SarawakBizOps.Api/   ASP.NET Core Web API + EF Core + Identity
└── SarawakBizOps.Web/   React + TypeScript office console (Admin/Manager/ServiceStaff/Warehouse)
```

## What's included

| Design doc reference | What's built |
|---|---|
| Section 5 — Identity | `ApplicationUser : IdentityUser`, JWT issuance, role seeding |
| Section 6 — Core Data Model | All 10 entities (Customer, Equipment, ServiceRequest, WorkOrder, Part, WorkOrderPart, InventoryTransaction, WorkOrderPhoto, ServiceReport, ApplicationUser) |
| Section 7 — Relationships | Full `ApplicationDbContext` with Fluent API configuration for every FK, unique index and cascade rule |
| Section 12/13 — API Design | `/api/auth/*`, `/api/customers/*`, `/api/equipment/*` — DTOs, role-based `[Authorize]`, consistent error responses |
| Section 16 — Mobile vs Web | The office-facing web app: login, dashboard shell, Customers CRUD, Equipment CRUD |
| Section 21/22 — Stack & Structure | React + TypeScript (Vite), matching the `Controllers/Data/Models/DTOs/Services/Middleware` backend layout |
| Roadmap Phases 1–4 | Solution setup, Identity, entities, JWT auth, and a first web UI slice |

## What's deliberately NOT included yet

Following your own design doc's build strategy (Section 24 — *"implement one vertical slice
at a time"*), these are next, not missing by accident:

- **ServiceRequest / WorkOrder engine** (Phase 5) — the state-machine logic in Sections 8–9, and
  its corresponding web pages
- **Inventory issuing + concurrency handling** (Phase 6) — Section 10's atomic issue-on-add rule
- **Equipment history, ServiceRequest CRUD, Update/history endpoints**
- **React Native mobile app** for technicians (Phase 8)
- **AI features, PDF report generation, Docker/CI-CD** (Phases 9–12)
- **Manager dashboard analytics** — the current dashboard shows real customer/equipment counts
  as a placeholder; `/api/dashboard/summary` (Section 12) doesn't exist yet

---

## Part 1 — Backend (`src/SarawakBizOps.Api`)

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (targets `net8.0` — bump `TargetFramework`
  in the `.csproj` if you're on a newer SDK, the code itself doesn't need to change)
- SQL Server LocalDB (ships with Visual Studio) or any SQL Server instance

### Setup

```bash
cd src/SarawakBizOps.Api

# 1. Restore packages
dotnet restore

# 2. Set the JWT signing key OUTSIDE the repo (never commit a real secret
#    in appsettings.json — the placeholder there is intentionally unusable)
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "a-long-random-string-at-least-32-characters"

# Optional: override the seeded admin credentials the same way
dotnet user-secrets set "Seed:AdminPassword" "something-only-you-know"

# 3. Point ConnectionStrings:DefaultConnection at your SQL Server if you're
#    not using the default LocalDB instance

# 4. Create and apply the first migration
dotnet ef migrations add InitialCreate
dotnet ef database update

# 5. Run it (fixed port via Properties/launchSettings.json — see note below)
dotnet run
```

This opens `http://localhost:5080/swagger` (the `http` launch profile is the default — see
"Known harmless warning" below for why HTTP is the easy path locally). Click **Authorize**,
log in via `POST /api/auth/login` with the seeded admin (`admin@sarawakbizops.local` / the
password you set above, default `ChangeMe123!`), paste the returned token as `Bearer {token}`,
and you can now call the Customer/Equipment endpoints as an authenticated Admin.

If `dotnet ef` isn't found: `dotnet tool install --global dotnet-ef`.

**Known harmless warning:** running the `http` profile prints a one-line warning about not
being able to determine an HTTPS redirect port — safe to ignore for local dev. To avoid it
entirely, run `dotnet run --launch-profile https` instead, after trusting the dev cert once
with `dotnet dev-certs https --trust`.

### Design decisions carried over from v1.1

- **No custom password/role fields** — Identity owns all of that (Section 5).
- **`RowVersion` concurrency tokens** on `WorkOrder` and `Part` — not in the original v1.1
  table listing, added here because Section 10 and Section 18 both call for transactional,
  concurrency-safe writes on exactly those two tables (two technicians issuing the last unit
  of stock, or racing on the same work order's status). `[Timestamp]` gives EF Core's optimistic
  concurrency check for free — a conflicting save throws `DbUpdateConcurrencyException` instead
  of silently overwriting someone else's change.
- **Repository layer omitted** — Section 1 of your review explicitly made this optional;
  services talk to `ApplicationDbContext` directly, which is clearer for an app this size.
- **Manual DTO mapping**, no AutoMapper — one less dependency, and the mapping is visible and
  easy to extend when you add ServiceRequest/WorkOrder DTOs next.
- **`Properties/launchSettings.json` added** (not in the original file list) — fixes the dev
  port at `5080`/`5443` so the web app's `.env.example` can point somewhere real instead of
  guessing at a random Kestrel port.

---

## Part 2 — Web app (`src/SarawakBizOps.Web`)

A React + TypeScript (Vite) single-page app for Admin / Manager / Service Staff / Warehouse
Staff. No UI library or CSS framework — plain CSS with design tokens, styled to match the
navy/steel identity from the System Design Document PDF rather than a generic template look.

### Prerequisites

- [Node.js](https://nodejs.org/) 18+ and npm

### Setup

```bash
cd src/SarawakBizOps.Web

npm install
cp .env.example .env.local   # adjust VITE_API_BASE_URL if your API runs elsewhere
npm run dev
```

Open `http://localhost:5173`. Log in with the same seeded admin account as above. The backend's
CORS policy already allows `http://localhost:5173`, so this works with zero extra config as
long as the API is running.

### What's on screen

- **Login** — calls `POST /api/auth/login`, stores the JWT + role list in `localStorage`
- **Dashboard** — a real (if minimal) view: live counts of customers and equipment
- **Customers** — list, create, and edit, gated to Admin/ServiceStaff (matches the backend's
  `[Authorize(Roles = "Admin,ServiceStaff")]`); everyone else gets read-only
- **Equipment** — list (filterable by customer), create with a customer picker, status shown
  as a colored badge

### Design decisions

- **No axios, no state-management library** — a ~40-line `fetch` wrapper (`src/api/client.ts`)
  and React Context (`src/auth/AuthContext.tsx`) cover everything this slice needs. Add a real
  data-fetching library (TanStack Query is a good fit) once ServiceRequest/WorkOrder bring more
  cross-page cache invalidation than `useEffect` can comfortably handle.
- **UI hides what the API would reject** (e.g., the "New customer" button, for a Technician role)
  as a courtesy, not as security — the real authorization check is server-side, matching Section
  13's API rule that a client can't be trusted to enforce its own permissions.
- **System font stack**, not a webfont — this console lives in a browser tab all day; instant
  load beats brand personality for an internal tool.

### A note on verification

I don't have network access in the environment I built this in, so I couldn't run `npm install`
or a real `tsc`/`vite build` against the actual `react`/`react-router-dom` packages. I did
type-check every file against hand-written minimal stubs for those packages to catch internal
bugs (typos, mismatched prop names, inconsistent types between files) — that came back clean —
but a stub can't catch everything a real `@types/react` would. Treat your first `npm run dev` as
the real test; if something doesn't compile, it's most likely a version-specific typing detail,
not a logic error, and should be quick to fix.

---

## Suggested next step

Pick up Phase 5 using this code as the template on both sides:
- **API:** a `ServiceRequestsController` + `ServiceRequestService` (create → approve/reject →
  assign, per Section 8), then the `WorkOrder` state machine (Section 9) with the transition
  guards from Section 9's table.
- **Web:** a `ServiceRequestsPage` following the exact shape of `CustomersPage.tsx` — list,
  create form, and role-gated actions for the approve/reject/assign buttons.

The Customer/Equipment slice on both sides shows the pattern to repeat: same layering, same
role-restriction approach, same error-handling style.
