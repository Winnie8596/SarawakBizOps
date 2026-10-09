# AI module — impact assessment (AI-0)

What each phase of `AIplan.md` adds or changes in the existing app, so nothing surprises a reviewer. Decisions are in
`decisions.md`; this file only maps them onto the code. Written 2026-10-09 against `main` at `25d010f`.

## Summary

- **No change to non-AI features.** Every new table, route and service is AI-only. The one shared file touched beyond
  wiring is `docker-compose.yml`/the API `Dockerfile` (the knowledge base must reach the container, see Risks).
- **One breaking API change:** `TechnicianAssistanceDto.SuggestedChecks` changes from `string[]` to an array of
  objects (AI-2). The only consumer is the web app's `AssistantPanel`, updated in the same phase.
- **Two migrations:** knowledge base tables (AI-1) and interaction logging (AI-4). Both add tables only.
- **Three new routes:** `GET /api/ai/chunks/{id}` (AI-2), feedback (AI-4), `GET /api/ai/interactions` (AI-4).
- **One new project:** `SarawakBizOps.AiEval` (AI-2), a console app outside the API's Docker image.

## Per phase

### AI-1 — Knowledge base and ingestion

| Kind | Item |
|---|---|
| New folder | `knowledge-base/*.md` (synthetic manuals) and its `README.md` |
| New entities | `AiSourceDocument`, `AiSourceDocumentModel`, `AiDocumentChunk` (+ EF configurations in `Data/Configurations/`) |
| Migration | `AiKnowledgeBase`: three tables in `dbo`, indexes on (status), (normalised brand, model) and (document, ordinal). No change to existing tables |
| New services (`Services/Ai/`) | `IdentifierNormaliser`, `MarkdownChunker`, `IEmbeddingClient` + `OpenAiCompatibleEmbeddingClient` + `MockEmbeddingClient`, `IDocumentIngestionService` |
| Changed | `AiOptions` (chunking, embedding model, dimension); `AiServiceRegistration`; `Program.cs` (Development-startup ingestion, the `ingest-kb` command); `appsettings.json` (`Ai` defaults) |
| New tests | Chunker, normaliser, ingestion (needs SQL, so it uses `ApiFactory`) |
| Unchanged | `openapi.json` (no routes), web app, existing tests |

### AI-2 — Grounded AI-01, vector-only

| Kind | Item |
|---|---|
| New services | `IVectorStore` (in memory, `IMemoryCache` — already registered), `IRetrievalService` |
| Already built | `SafetyTerms` + `SafetyTermsTests` (ahead of plan, 2026-10-09) |
| Changed | `TechnicianAssistantContext` (+ `ManualSections`; `MaxPastCases` 5 → 10; doc comment on other customers' jobs), `TechnicianAssistantContextBuilder` (same brand + model across customers), `TechnicianAssistantPrompt` (`ai01.v2`), `TechnicianAssistantOutput` (check objects, validation order 1–7), `MockLlmClient` (answers in the new shape), `AiService` |
| DTO | `TechnicianAssistanceDto`: `SuggestedChecks` becomes `List<AssistantCheckDto>`; add `Citations`, `Notices`, `SafetyStepsWithheld`. **Breaking** for the web client |
| New route | `GET /api/ai/chunks/{id}` — Technician, Manager |
| Contract | `openapi.json` regenerated; `npm run gen:api` → `src/api/schema.d.ts` |
| Web | `AssistantPanel.tsx` (source labels, safety first, notices, citation side panel); `Assistant.test.tsx` (7 tests) updated |
| Existing tests to update | `AiRulesTests` (check strings become objects), `TechnicianAssistantTests` (9 tests), `OpenApiDocumentTests` (snapshot), `EndpointAccessTests` (+1 row) |
| New project | `SarawakBizOps.AiEval` (console), added to `SarawakBizOps.sln`; `eval/` folder with questions, `cache/`, `results/` |

### AI-3 — Hybrid search

| Kind | Item |
|---|---|
| New service | `IKeywordIndex` (BM25, typed by the owner) |
| Changed | `IRetrievalService` (RRF, identifier gate, `RetrievalMode`), `AiOptions` (BM25 and RRF settings) |
| Unchanged | Database, routes, DTOs, web app |

### AI-4 — Abstention, logging, feedback

| Kind | Item |
|---|---|
| New entities | `AiInteraction`, `AiInteractionFeedback` |
| Migration | `AiInteractions`: two tables, index on `CreatedAt` for retention |
| New services | `AiInteractionCleanupService` (`BackgroundService`, modelled on `OrphanFileCleanupService`) |
| Changed | `AiService` (writes an interaction for every outcome, including failures), `AiOptions` (`Pricing`, retention), `AnalyticsService`/`DashboardSummaryDto` (the AI card), the Manager dashboard page |
| New routes | `POST /api/ai/interactions/{id}/feedback` (the asking user only), `GET /api/ai/interactions` (Admin, paged) |
| Contract | `openapi.json`, web types, `EndpointAccessTests` (+2 rows) |
| Web | Helpful / Not helpful in `AssistantPanel`; dashboard card |

### AI-5 — Deploy and write-up

`.env.example` and `docker-compose.yml` (variable names only), the API image (see Risks), README section.

## Database and permissions

- New tables live in `dbo`, so `create-logins.sql` needs **no change**: the app login's schema-level grants cover
  them, and the AI reader's `db_datareader` lets retrieval read chunks through the SELECT-only connection.
- **Ingestion must use the app connection**, not `AiReadOnlyData` — the AI reader cannot write (by design).
  Interaction logging (AI-4) is the same: written through the app connection.
- Embeddings are `varbinary(max)`: 768 floats × 4 bytes ≈ 3 KB a chunk, about 1.5 MB for 500 chunks.

## Risks found while mapping

| Risk | Effect | Proposed handling |
|---|---|---|
| **The API image does not contain `knowledge-base/`.** `src/SarawakBizOps.Api/Dockerfile` copies only the API folder, and the folder lives at the repo root | `ingest-kb` in a container finds no files; Development startup in Docker ingests nothing | AI-1: copy `knowledge-base/` into the image (the build context is already the repo root), with a configurable `Ai:KnowledgeBasePath` |
| **The chunk endpoint would share the AI rate limit.** `AiController` has `[EnableRateLimiting(Ai)]` on the class (10 permits a minute) | Opening a few citations, plus one question, exhausts a technician's minute | AI-2: put `GET /api/ai/chunks/{id}` on its own controller or mark it `[DisableRateLimiting]`, since it calls no model; covered by a test |
| Breaking DTO change | A web build older than the API shows `[object Object]` for checks | Ship API and web together (they deploy together today); no versioning needed for a single client |
| Wider `pastCases` (G6) | Other customers' jobs reach the model | Context builder selects no customer fields; a test asserts the serialized context contains none |
| New migrations on startup | `Database:MigrateOnStartup` runs them in Docker | Additive only; `DemoSeedingTests` already proves migrate-on-empty |

## Test-suite effect

Baseline on `main` (2026-10-09): API 1,105 tests (all pass; the database ones need Docker), plus 102 `SafetyTerms`
tests; web 168. Phases AI-1 to AI-4 add their tests; the existing ones listed above change in AI-2 and AI-4 only.
