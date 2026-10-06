# Decision log

The System Design Document (SDD v1.1) is the original design. [`prd.md`](../prd.md) amends it (design decisions
D-01…D-08) and listed the questions left open; [`plan.md`](../plan.md) records, phase by phase, how each slice was
built. This page is the one place that says what was decided, when, and why — in short, with a link to the detail.

## Open Question 7 — how the SDD is kept up to date

**Decision (Phase 15): the PRD stays the amendment record; there is no SDD v1.2.** Where the PRD and the SDD disagree,
the PRD wins (README, *Documents*). The PRD's §8 lists every deviation from the SDD, each phase's *Decisions taken*
table in `plan.md` records the finer choices, and this log indexes them.

*Why:* a v1.2 would copy the SDD to change a handful of paragraphs, and two long documents that must agree is the
problem the PRD already solves. The code, its tests and these three documents are what a reader checks; a fourth
copy of the design would only drift.

## Design decisions taken in the PRD

| ID | Decision | Where it shows |
|---|---|---|
| D-01 | The technician flow is a **responsive web view** (`/tech`); React Native is P2 stretch | Phase 4 onwards; Phase 16A not started |
| D-02 | AI behind a **provider-agnostic interface** with a deterministic **mock** by default | `ILlmClient`, `MockLlmClient` (Phases 13–14) |
| D-03 | Files in an **Azure-compatible emulator** behind `IFileStorage` | Azurite + `AzureBlobFileStorage` (Phase 8) |
| D-04 | Currency RM; **UTC stored, MYT shown**; English only | Every page; months bucketed in MYT (Phase 11) |
| D-05 | **No AuditLog table, no reopen flow**; timestamps + structured logs; approval locks | Central lock guard (Phase 5) |
| D-06 | **Minimal dependencies**: a library only when a requirement needs it, recorded in the README | No PDF, chart, UI, state or mocking library |
| D-07 | Public GitHub repository, MIT | Licence in place; **the repository stays private until the owner decides** |
| D-08 | `RowVersion` on `WorkOrder`/`Part` (and, from Phase 5, `ServiceRequest`) | Required on every mutating call |

## Open questions and how they were settled

| # | Question | Decision | When |
|---|---|---|---|
| 1 | AI provider | **Mock everywhere by default** (dev, tests, CI, Docker, demo) plus one hand-rolled client for any OpenAI-compatible `/chat/completions` API (Gemini, Groq, Ollama, OpenAI, Azure OpenAI by configuration; key from user-secrets or `Ai__ApiKey`). | Phase 13 |
| 2 | Storage emulator | **Azurite**, through `Azure.Storage.Blobs` — the same code talks to real Azure Blob Storage. | Phase 8 |
| 3 | PDF library | **None**: a small PDF 1.4 writer in `Services/Reports/Pdf`. QuestPDF's licence is revenue-gated; PDFsharp needs font files in Linux containers. Cost: Latin-1 text only. | Phase 10 |
| 4 | Live deployment | **Done as an owner-run demo, outside CI**: the two images run on Azure Container Apps (scale to zero, so the first request after a quiet spell takes 30–60 s), with Azure SQL's free offer and a storage account. The deployment differs from `docker compose` only in nginx's upstream address and in its own secrets; no deployment files or credentials are committed. | 2026-10-01 (a Phase 12 build); redeployed on the owner's schedule |
| 5 | React Native | **Not started.** Phase 16A stays a stretch goal; the responsive technician view covers the whole field workflow at 360 px. | Phase 15 |
| 6 | Dashboard charts | **Hand-rolled HTML/CSS** (a table and a list sized with CSS), readable to screen readers and tests. | Phase 11 |
| 7 | SDD v1.2 or PRD as the amendment | **PRD as the amendment record** (above). | Phase 15 |
| 8 | Stock issue under concurrency *(raised in `plan.md`)* | **One conditional `UPDATE`** (`… WHERE QuantityOnHand - @q >= 0`) in the same transaction as the line and the ledger row; no retry loop, no `UPDLOCK`. | Phase 7 |
| 9 | Conflict-token transport *(raised in `plan.md`)* | **`rowVersion` in the body of every mutating call**, checked in the same conditional `UPDATE … WHERE Status = @from AND RowVersion = @rv`, so status races and same-status edits are one 409. | Phase 5 |

## Phase 15 decisions (release sweep)

| Topic | Decision |
|---|---|
| Customers and equipment | Reads are **office-only** (Admin, Manager, ServiceStaff), like the pages that show them. Until now any signed-in role could list the customer directory; a Technician sees a customer and machine only through their own job (BR-02), and the warehouse has no use for them. |
| Deny by default | A **fallback authorization policy** requires a signed-in user on any endpoint without its own rule. Only login and the two health checks are public, and a test pins that list. |
| AC-11 over the whole surface | `EndpointAccessTests` now has a row for **every** route (56), and a test compares the table with the routes the app actually maps, so a new endpoint without a row fails the build. |
| CORS | None needed for the shipped app (same origin through nginx). Outside Development the allowed origins come from `Cors:AllowedOrigins`, empty by default; the hard-coded localhost origins are gone. |
| Browser hardening | nginx sends a Content-Security-Policy (same origin only; `blob:`/`data:` for the signature, photos and PDFs), `X-Content-Type-Options`, `X-Frame-Options: DENY`, a referrer policy and a permissions policy. The API adds HSTS when it terminates HTTPS itself (not in Docker, where nginx speaks plain HTTP). |
| Dependencies | Upgraded to clear every advisory: Vite 8, Vitest 5, `@vitejs/plugin-react` 6, React Router 7 (library mode, no code change), ASP.NET Core/EF Core 8.0.31 patches and newer test tooling. `npm audit` and `dotnet list package --vulnerable --include-transitive` report nothing. |
| Secrets | gitleaks over the **full history** finds only Azurite's published development key (accepted in `.gitleaksignore`); CI now runs the same scan on every push. |
| Pagination | **Not added.** Every list is bounded by the demo's scale (tens to a few hundred rows) and the long ones have server-side filters (requests and jobs by status, the ledger by part, type, job and date). Paging changes every list's contract and page, so it is on the V2 list rather than half-done now. |
| Seed | Grown to the PRD's scale — **10 customers, 30 equipment** — with no jobs on the new rows, so every hand-computed dashboard figure is unchanged. A test proves the seed has a record in every reachable state. |

## After Phase 15

| Topic | Decision |
|---|---|
| .NET version | **Moved from .NET 8 to .NET 10** (LTS, supported to November 2028) before .NET 8 support ends on 10 November 2026. .NET 9 was skipped: its support ends on the same day. The two projects target `net10.0`, the ASP.NET Core/EF Core packages are 10.0.12, the API image is built on `sdk:10.0`/`aspnet:10.0` and CI installs `10.0.x`. Swashbuckle stays on 6.6.2 (the newer major moves to OpenAPI v2 for no gain here). No code changes were needed. |
| Reverse proxy | **`UseForwardedHeaders`, first in the pipeline**, trusting `X-Forwarded-For`/`-Proto` only from hops in `ForwardedHeaders:KnownNetworks` (the private ranges by default: nginx on the compose network, a platform ingress). It walks back past every trusted hop to the first outside address, so the API sees the real caller and a forged entry in front of it is ignored. |
| Connection string | **Checked at startup**, like the JWT key and the storage connection: a missing `ConnectionStrings:DefaultConnection` stops the API with a hint instead of failing on the first query. |
| Unhandled exceptions | The hand-written middleware became **an `IExceptionHandler` under `UseExceptionHandler`**: the framework clears a half-written response and handles client disconnects, and the handler refuses to write once `Response.HasStarted`. Same 500 body as before (ProblemDetails with a `traceId`), now covered by a test. |
| Database logins | **The API no longer connects as `sa` in Docker.** `scripts/sql/create-logins.sql`, run as `sa` by a one-shot `db-init` service, creates the database and two logins: the app's (dbo data read/write, the report-number sequence, and `db_ddladmin` because the demo migrates on startup; no server rights) and a `db_datareader`-only one behind the optional `ConnectionStrings:AiReadOnly`, so SQL Server refuses an AI write even past the in-app `ReadOnlyGuard`. `DemoApiFactory` runs the same script, so the demo tests run on these logins. A production deployment would migrate with a separate login and drop `db_ddladmin` from the app's. |
| Dependency updates | **Dependabot**, weekly and grouped (ASP.NET Core/EF Core together, test tooling, web minor+patch), for NuGet, npm, GitHub Actions and the Dockerfiles. Major versions of the .NET packages and images are ignored: they arrive with a planned .NET upgrade, like this one. |
| Folders | `DatabaseHealthCheck` moved to `HealthChecks/` and the exception handler to `ErrorHandling/`; neither was middleware. |
