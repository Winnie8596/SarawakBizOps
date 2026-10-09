# AIplan.md — SarawakBizOps AI Module: grounded technician assistant

**Decisions:** `docs/ai/decisions.md` (G1–G11 are referenced below). This plan and that file are the source of truth;
the "Addendum" the first draft cited was never written.
**Goal:** Ground the existing AI-01 technician assistant in equipment manuals, with cited checks, a refusal rule for
safety steps, a reproducible evaluation with measured results, and a write-up fit for an AI fullstack portfolio.
**Out of scope:** Job triage, an upload API/admin screen for documents, PDF ingestion, a separate vector database,
streaming, React Native UI, LLM-as-judge, a full admin metrics page.

---

## 1. Rules for the agent

1. **`docs/ai/decisions.md` wins.** If this plan and it disagree, follow the decisions file and fix the plan.
2. **One phase at a time.** Do not start a phase until the owner approves the previous checkpoint.
3. **Stop and ask** on anything marked Open in `decisions.md`, or a change that would affect non-AI features.
4. **Inspect before you add.** Reuse `ILlmClient`, `AiOptions`, `AiReadOnlyData`, `TechnicianAssistantContextBuilder`,
   `AssistantPanel`, the existing test factories and conventions. Never duplicate WorkOrder, Equipment, Customer, User or
   role models. "Job" means `WorkOrder`; "Supervisor" means Manager.
5. **New AI code lives in `src/SarawakBizOps.Api/Services/Ai/`** (one API project, no new layers). Services depend on
   interfaces (`ILlmClient`, `IEmbeddingClient`, `IVectorStore`, `IKeywordIndex`), never on a provider.
6. **Defaults come from §6 and live in configuration** (`Ai` section, extending `AiOptions`), not in constants.
   Exception: `SafetyTerms` is a static, unit-tested list in code (G13), so configuration cannot switch it off.
7. **Tests use the mock provider only.** No test calls a real provider or needs the network.
8. **Never invent results.** Every number in a report or README comes from a committed file under `eval/results/`;
   otherwise it is `TBD`.
9. **No secrets in the repo.** Keys come from user-secrets or environment variables; names only in `.env.example`.
10. **Small commits**, `ai(<phase>): <what changed>`, e.g. `ai(AI-1): add markdown chunker`.
11. **Keep existing tests green** (API and web) at the end of every phase. New routes get a row in
    `EndpointAccessTests`; API shape changes regenerate `openapi.json` and the web types (`npm run gen:api`).
12. **Record decisions** — tuned defaults, assumptions, deviations — in `docs/ai/decisions.md` with date and evidence.
13. **Coaching:** the owner types BM25 (AI-3) and other parts they ask for; the agent explains and reviews instead of
    writing those.

### Branching

* Merge the .NET 10 upgrade (`upgrade-dotnet-10`) into `main` first.
* One branch per phase from `main`, e.g. `feature/ai-1-ingestion`; one PR per phase, whose description is the
  checkpoint report (§4).

---

## 2. Phase overview

Built as a thin slice first, then improved with measurements (G11).

| Phase | Name | Release | Main output | Estimate |
|---|---|---|---|---|
| AI-0 | Discovery & decisions | — | `decisions.md`, this plan, impact notes | 2–3 days (mostly done) |
| AI-1 | Knowledge base & ingestion | v1 | Synthetic manuals, chunks with embeddings in SQL, ingestion service | ~1 week |
| AI-2 | Grounded AI-01 (vector-only) | **v1 — ready to demo** | Cited checks in AI-01, citation viewer, eval harness, baseline numbers | ~1.5 weeks |
| AI-3 | Hybrid search, measured | v2 | BM25 + RRF, chunk-size and threshold experiments, before/after numbers | ~1 week |
| AI-4 | Abstention, logging, feedback | v3 | Refusal layers, `AiInteraction`, feedback, dashboard card, retention, final test-set run | ~1 week |
| AI-5 | Deploy & write-up | v3 | Deployed knowledge base, README "AI Module" | 3–4 days |

About 5 weeks part-time. After AI-2 the feature can be demoed and deployed. The owner starts applying in
January 2027, which leaves room for all of v1–v3 and the write-up.

---

## 3. Phases

### Phase AI-0 — Discovery & decisions

**Goal:** Know where the module fits; no production code.

- [x] Map the existing AI code: `IAiService`, `ILlmClient` (mock + OpenAI-compatible), `AiOptions`, `AiReadOnlyData`
      and its SELECT-only login, `TechnicianAssistantContextBuilder`, `AssistantPanel`, rate limiting.
- [x] Map entities and roles: `WorkOrder`, `Equipment` (free-text `Brand`/`Model`), roles Admin, Manager, ServiceStaff,
      Technician, WarehouseStaff; no Supervisor, no escalation or notification mechanism.
- [x] SQL Server version: `mssql/server:2022-latest` locally (no `VECTOR` type), Azure SQL when deployed.
- [x] Resolve design decisions G1–G11 (`docs/ai/decisions.md`).
- [ ] Write `docs/ai/impact-assessment.md`: files to add/change per phase, new entities and the migration, new routes,
      the AI-01 schema change and its effect on `AssistantPanel`, `openapi.json` and existing tests.
- [ ] Merge the .NET 10 upgrade into `main`.

**Checkpoint:** Owner approves the impact assessment.

---

### Phase AI-1 — Knowledge base & ingestion (v1)

**Goal:** `knowledge-base/` loads into SQL as chunks with embeddings, repeatably and cheaply (G8).

**Knowledge base content** (synthetic; AI may draft, owner reviews — G5)

- [ ] `knowledge-base/*.md`, each with front matter: `slug`, `title`, `brand`, `models` (one or more), `version`,
      optional `supersedes` (a slug).
- [ ] Manuals for at least 4 seeded models (e.g. Norvik NC-450 chiller, Hartmann HC-75 compressor, Kestrel KB-10T
      boiler, Tanjung TL-35D forklift — see `DemoSeeder`), each with a Troubleshooting section containing error codes,
      safety precautions (isolation/lockout), and part numbers that exist in the parts catalogue where possible.
- [ ] One model with v1 and v2 manuals (v2 changes a procedure and `supersedes` v1).
- [ ] One manual covering several models (G3).
- [ ] One document with a prompt-injection attempt, marked as an evaluation fixture.
- [ ] At least one section in Bahasa Malaysia or mixed terms.
- [ ] One error code shared by two models with different meanings (tests the equipment filter, G18).
- [ ] At least one seeded model that has work orders but **no** manual (tests the History/General path, G18).
- [ ] `knowledge-base/README.md`: all content is synthetic; list each document, its models and version.

**Backend**

- [ ] Extend `AiOptions` with the §6 defaults (chunking, retrieval, embedding model and dimension).
- [ ] Entities + EF configuration + one migration: `AiSourceDocument` (slug, title, version, content hash,
      chunking configuration ID, status `Active | Superseded | Removed`), `AiSourceDocumentModel` (normalised brand + model, many per document), `AiDocumentChunk`
      (document, ordinal, section heading path, text, token count, embedding bytes, embedding model).
- [ ] Shared normaliser for brand/model/identifiers (upper case, strip spaces and dashes) — used by G3, G6 and G7.
- [ ] `IEmbeddingClient`: OpenAI-compatible `/embeddings` (same base URL and key as chat, `dimensions` set) and a mock
      (deterministic hash-based vectors, fixed dimension). Choose and record the embedding model (Open item).
- [ ] Markdown chunker: heading → paragraph → sentence splitting, 400/50 tokens, minimum 50 (merge small trailing
      pieces), heading path as `SectionHeading`. Tokens counted as `ceil(characters / 4)` (G18).
- [ ] `IDocumentIngestionService`: read the folder, parse front matter, skip a file only when its content hash **and**
      the chunking configuration match what is stored (G15), re-chunk and re-embed the others in batches, all in one
      transaction per document (a failure leaves the previous state). After all files: apply `supersedes`, and mark
      documents whose file is gone as `Removed` (G15). Neither `Superseded` nor `Removed` is ever retrieved.
- [ ] Callers: Development startup, a CLI command (e.g. `dotnet run -- ingest-kb`) for deployments, and (AI-2) the
      evaluation runner.

**Tests**

- [ ] Chunker: heading boundaries, overlap, small trailing piece merged, `SectionHeading` correct.
- [ ] Normaliser: `kb-10t`, `KB 10T`, `KB-10T` → `KB10T`.
- [ ] Ingestion: new file stored; unchanged file skipped (no embedding calls); the same file re-chunked when the
      chunking configuration changes; changed file replaced; v2 hides v1; a deleted file's document becomes
      `Removed`; a failure mid-document leaves the previous chunks intact; bad front matter is reported, not
      half-stored.
- [ ] Existing suites pass.

**Checkpoint:** PR with 5 example chunks (heading path, token count, text excerpt).

---

### Phase AI-2 — Grounded AI-01, vector-only (v1, ready to demo)

**Goal:** AI-01 answers with checks labelled `Manual` (cited), `History` (verified past jobs) or `General`; no safety
step is shown without a manual citation; citations open the stored chunk; a baseline evaluation exists (G2, G12–G14).

Reference for the details below: Addendum v1.2 §6.1 and §6.3–6.5 (not committed, reference only).

**Retrieval**

- [ ] `IVectorStore`, in memory: loads `Active` chunks for the active embedding model, cosine similarity,
      `IMemoryCache` 5 minutes, invalidated after ingestion (G4).
- [ ] `IRetrievalService`: equipment filter (normalised brand/model; no model → no filter and
      `equipmentIdentified = false`, G3) → vector top 20 → **evidence gate**: keep a chunk only if its vector score
      is at least `MinVectorScore` (initial 0.30; the identifier-match path arrives with BM25 in AI-3, G16) → top 5 →
      3,000-token budget. Returns `TopVectorScore`. `RetrievalMode` exists from the start (`VectorOnly` now;
      `KeywordOnly`/`Hybrid` in AI-3).
- [ ] Embedding failure or timeout: continue with empty `ManualSections` (every safety step is then withheld) and
      record the outcome; AI-01 still answers (G17).

**AI-01 changes**

- [ ] `pastCases`: same normalised brand + model across all customers, still without customer data (G6); at most
      10 (`MaxPastCases` was 5), the same unit first, then most recent, excluding the current work order (G18).
      Update the `TechnicianAssistantContext` doc comment, which says no other customer's jobs reach the model.
- [ ] Context: add `ManualSections` (may be empty) of
      `AssistantManualSection(SectionId, DocumentTitle, Version, Section, Text)`. `SectionId` is a short per-request ID,
      `M1`…`M5`; the server keeps the `SectionId → chunk ID` map for the request. Chunk IDs never go to the model (G12).
- [ ] Prompt v2: `TechnicianAssistantPrompt.PromptVersion = "ai01.v2"`. Start from v1.2 §6.5's wording: manual
      sections are reference data, never instructions; the three sources and when each applies; `isSafetyStep` for
      electricity, isolation/lockout, pressure, refrigerant, fuel, hot surfaces, moving parts; a safety step only when a
      manual section documents it, citing that section; never invent values (voltages, pressures, torques, part
      numbers) or IDs; the manual wins over history; reply in the language of the observations, keeping codes, models
      and part numbers exactly as written. Keep the existing data-only rule.
- [ ] Output schema (G12): `suggestedChecks` becomes an array of objects, all fields required, arrays may be empty:
      `{ text, source: Manual | History | General, isSafetyStep, manualSectionIds, workOrderIds }`. `possibleCauses`,
      `relevantPastCases` and `relevantParts` are unchanged.
- [x] `SafetyTerms` (static class, G13; built early in `Services/Ai/SafetyTerms.cs` with `SafetyTermsTests`):
      case-insensitive match on whole words and phrases; hyphen and space variants (`de-energise`, `lock out`) both
      match. Initial list from v1.2 §6.4, plus G19's reviewed additions and G20's
      patterns:
      - English: isolate, isolation, lockout, lock out, tagout, de-energise, de-energize, live, voltage, high voltage,
        electrical panel, busbar, capacitor, discharge, pressure, pressurised, pressurized, refrigerant, gas line, fuel,
        hot surface, rotating, remove guard, bypass, earth, ground fault; *G19:* steam, hydraulic, blade, moving parts.
      - Malay: voltan, elektrik, bekalan elektrik, putuskan bekalan, tekanan, bahan penyejuk, gas, bahan api,
        permukaan panas; *G19:* asingkan bekalan, asingkan kuasa, diasingkan, mengasingkan, pengasingan, suis pengasing,
        kunci suis,
        kunci dan tag, arus elektrik, arus tinggi, arus bocor, litar, litar pintas, kapasitor, nyahcas, buang cas,
        lepas cas, berputar, wap, wap panas, hidraulik, bilah, renjatan, potong bekalan, suis utama, tiga fasa.
      - Loanwords (*G19*): LOTO, ampere, amp, freon, MCB, DB, breaker.
      - Patterns (*G20*): voltage (`415V`, `11 kV`), pressure (`150 psi`, `8 bar`, `600 kPa`), refrigerant code
        (`R410A`, `R-22`).
      - Tests: every term and pattern matches; whole-word only (`tekan` and `tekanan` differ); narrowed words do
        **not** match on their own (`asingkan skru`, `kunci 10mm`, `arus udara`); phrases inside longer sentences
        (`bahagian berputar`, `kipas berputar`, `tutup suis utama`).
- [ ] Validation in `TechnicianAssistantOutput`, applied to every check **in this order**:
      1. Drop `manualSectionIds` not in this request's map and `workOrderIds` not in this request's `pastCases` (G14).
      2. `Manual` with no section ID left → `General` (G12). `History` with no work order ID left → `General` (G14).
      3. Safety step = the model's `isSafetyStep` **or** `SafetyTerms.Matches(text)` (G13).
      4. A safety step that is not `Manual` with at least one valid section ID is **removed**; count the removals.
      5. If any were removed, add the notice: "A safety-related step was withheld because no documented procedure is
         on file for this equipment. Contact a Manager before proceeding."
      6. Map section IDs to chunk IDs and build the response's citations from stored chunk metadata (document title,
         version, section heading), never from model text.
      7. The existing rules for causes, past cases and parts stay.

      The existing "no usable cause or check" refusal is checked **before** step 4: an answer whose checks were all
      withheld for safety is still an answer (causes plus the notice), not "AI unavailable".
- [ ] Response DTO: per check `source`, `isSafetyStep`, `citationIds` (chunk IDs) and `workOrderIds`; a `citations`
      list; `notices`; the count of withheld safety steps.
- [ ] `GET /api/ai/chunks/{id}` for the citation viewer: any Technician or Manager, no per-job check (manuals are not
      job data); `EndpointAccessTests` row.

**Web**

- [ ] `AssistantPanel`: each check shows its source — "Manual", "Past job" (linking to the work order where the role
      allows), or "General suggestion — not from a manual"; safety steps first and visually distinct; citation markers
      open a side panel with title, version, section heading and chunk text; notices above the checks. Plain text only.
- [ ] Regenerate `openapi.json` and web types.

**Evaluation harness** (G5, G9)

- [ ] `eval/questions.dev.jsonl` (about 25) and `eval/questions.test.jsonl` (15+), written by the owner in technician
      language; each with `expectedChunkRefs` (`{documentSlug}#{section-slug}`) or `expectNoEvidence`,
      `expectSafetyWithheld`, `referenceChecks` (the checks the author expects, for the manual review), and a category
      (error code, shared error code across models, procedure, safety, superseded manual, model without a manual,
      unsupported, wrong-equipment, Malay/mixed, injection) (G18).
- [ ] `SarawakBizOps.AiEval` console project: refuses `Ai:Provider = Mock`; ingests into an isolated database; resolves
      chunk refs; runs each question per retrieval mode; embedding cache under `eval/cache/` keyed by (model, SHA-256
      of text); writes `eval/results/{date}-{configId}.json` and `{date}-summary.md` with counts ("13/15").
- [ ] Run the **dev set** vector-only: Recall@5 by category. This is the baseline AI-3 must beat.
- [ ] **Do not run the test set** until AI-4.

**Tests (mock)**

- [ ] Superseded and removed chunks never retrieved; equipment filter applied; unknown model searches everything and
      says so.
- [ ] Evidence gate: a chunk below `MinVectorScore` is never sent; none passing → empty `ManualSections`, an answer
      with only `History`/`General` checks and every safety step withheld (G16).
- [ ] Embedding failure or timeout → AI-01 still answers without manual sections, safety steps withheld (G17).
- [ ] `pastCases`: other customers' jobs on the same model included without customer fields; same unit first; at
      most 10; current work order excluded (G6, G18).
- [ ] Section IDs: an ID outside this request's `M1`…`M5` map is dropped; cited sections map to the right chunk IDs;
      citations come from stored metadata, not model text.
- [ ] G12: a `Manual` check whose only section ID is invalid becomes `General`, never `History`.
- [ ] G13, one test per path: the model flags a step; the model does not flag it but `SafetyTerms` matches
      (English, e.g. "isolate the supply at the panel"; Malay, e.g. "putuskan bekalan elektrik dahulu"); a cited
      `Manual` safety step is kept; an uncited one, or one relabelled `General` in step 2, is removed with the notice;
      all checks withheld still returns an answer with the notice.
- [ ] `SafetyTerms`: every listed term matches in upper/lower case and with hyphen/space variants; whole words only
      (e.g. "gasket" does not match "gas", "delivery" does not match "live").
- [ ] G14: a `workOrderId` not in `pastCases` is dropped; a `History` check left with none becomes `General`.
- [ ] Chunk endpoint: any Technician or Manager may read any manual chunk (manuals are not job data, so no
      per-job check); unauthenticated → 401; other roles → 403.
- [ ] Cache invalidated after ingestion.
- [ ] Existing AI-01 tests updated; existing suites pass.

**Checkpoint:** Demo on local Docker; baseline summary committed. **v1 is done here.**

---

### Phase AI-3 — Hybrid search, measured (v2)

**Goal:** Hybrid retrieval justified by the project's own numbers (G7, G11).

- [ ] `IKeywordIndex`: BM25 in memory over the same cached chunks (k1 1.2, b 0.75). Tokenizer keeps identifiers whole
      and normalised. **Owner types this, with coaching.**
- [ ] `HasExactTermMatch`: an identifier in the question matched a chunk.
- [ ] Hybrid: BM25 top 20 + vector top 20 → Reciprocal Rank Fusion (k = 60) → evidence gate, now passing a chunk that
      matched an identifier from the question **or** scores at least `MinVectorScore` (G16) → top 5 → budget. Modes
      `KeywordOnly`/`VectorOnly`/`Hybrid`.
- [ ] Experiments on the **dev set**: keyword-only vs vector-only vs hybrid; chunk size 250/40 vs 400/50. Recall@5 per
      category, with one or two failure examples explained (e.g. `E14` vs `E41`).
- [ ] Calibrate `MinVectorScore` on the dev set, sweeping 0.20–0.50 in steps of 0.05 from the initial 0.30
      (correct "no evidence" on unsupported questions vs Recall@5 on answerable ones). Record the value and evidence in
      `decisions.md`.

**Tests**

- [ ] BM25: known small-corpus scores; identifiers match across formatting; ordinary words still count.
- [ ] Exact error code found and passes the gate when the vector score is low; RRF ordering deterministic with the
      mock.
- [ ] Existing suites pass.

**Checkpoint:** Before/after summary committed; owner confirms chunk size and threshold.

---

### Phase AI-4 — Abstention, logging, feedback (v3)

**Goal:** The assistant refuses correctly, every question is logged, and real-user signal is collected (G2, G10).

- [ ] Abstention:
      - **Layer 1 (retrieval):** the evidence gate (AI-2/AI-3, G16) already drops weak chunks; here the answer gains a
        notice that no manual section was found when none passed. It then has only `History` and `General` checks
        and every safety step is withheld (G13).
      - **Layer 2 (model):** the model may answer that the evidence is insufficient.
      - **Layer 3 (validation):** the AI-2 citation and safety rules.
- [ ] `AiInteraction` entity + migration: user, work order, question, retrieval mode, retrieved chunk IDs and scores,
      prompt version, models, outcome, abstention layer, tokens in/out, latency, estimated cost, timestamp. Written
      for every outcome, including provider failures.
- [ ] Cost estimate from `Ai:Pricing` (per million tokens; source date and MYR rate in `decisions.md`); null when
      usage is missing.
- [ ] Feedback: Helpful / Not helpful + optional comment (≤ 500 chars); one per user per interaction; only its owner.
- [ ] Retention: delete interactions older than 90 days on a timer like `OrphanFileCleanupService`.
- [ ] Manager dashboard card: questions this month, % refused, % helpful, p95 latency, estimated cost.
- [ ] `GET /api/ai/interactions` (Admin, paged) — no page.
- [ ] Run the **test set once** with the final configuration; commit the results; add the review sheet
      (`eval/results/{date}-review.csv`) for the owner to mark answer correctness and unsupported claims.

**Tests (mock)**

- [ ] Each abstention layer; malformed model JSON → unavailable, no retry; provider timeout/5xx → existing
      unavailable handling, job workflows unaffected.
- [ ] Interaction row for every outcome; feedback once per user, owner only; retention deletes only old rows.
- [ ] Dashboard card figures match the interaction rows; new endpoints in `EndpointAccessTests`.
- [ ] Existing suites pass.

**Checkpoint:** Test-set results and completed review sheet committed.

---

### Phase AI-5 — Deploy & write-up

- [ ] AI variables (embedding model, dimension, pricing) in `.env.example`, `docker-compose.yml` and deployment
      settings — names only.
- [ ] Ingest the knowledge base in the deployed environment with the CLI command.
- [ ] README "AI Module": Mermaid architecture diagram; retrieval pipeline; the source labels and safety rule;
      three-layer abstention; trade-offs (BM25 + in-memory vectors vs a vector database and SQL Server 2025 `VECTOR`;
      documents as code vs upload; data minimisation vs redaction; server-built citations); evaluation method (dev/test
      split, embedding cache); **measured** results linked to `eval/results/`; known limitations from `decisions.md`;
      next steps (upload API, triage, mobile UI).

**Checkpoint:** Owner checks §5 item by item on the deployed app.

---

## 4. Checkpoint report template (PR description)

```markdown
## Phase AI-<n> — <name>

### Done
- …

### Not done / deferred (with reason)
- …

### Deviations (also in docs/ai/decisions.md)
- …

### Tests
- New tests: <count>, all passing
- Existing suites: API <pass>/<total>, web <pass>/<total>

### Measured results (if this phase ran evaluation)
- Link to eval/results/<file>
- Key numbers copied from that file only

### Questions for the owner
- …

### How to verify
- Steps the owner can follow locally or on staging
```

---

## 5. Definition of done

- [ ] On a work order, AI-01 shows checks labelled Manual, Past job or General suggestion; manual checks cite sections
      that open the real stored chunk.
- [ ] A safety step without a manual citation never reaches the UI, whether the model flagged it or only the
      `SafetyTerms` list caught it (automated tests in English and Malay); the technician is told to contact a Manager.
- [ ] Invented section IDs and work order IDs never reach the UI (automated tests).
- [ ] Superseded documents and other equipment's manuals are never retrieved (automated tests).
- [ ] Exact error codes are found by BM25 even when the vector score is low (evaluation cases and a test).
- [ ] The evaluation reruns from the committed embedding cache with the same retrieval numbers; the summary has
      dev-set experiments (modes, chunk sizes, threshold) and one test-set run, as counts.
- [ ] Provider failure keeps the existing "AI unavailable" behaviour; job workflow tests still pass.
- [ ] Every question is logged; feedback works; logs older than 90 days are deleted.
- [ ] Docker Compose and the deployment work with secrets from configuration only.
- [ ] README documents architecture, trade-offs, evaluation method, measured results and limitations.

---

## 6. Defaults (configuration, `Ai` section)

Existing settings keep their current values (`TimeoutSeconds` 20, `MaxOutputTokens` 1200, `PermitsPerMinute` 10).

| Setting | Default | Notes |
|---|---|---|
| Chunk size / overlap / minimum | 400 / 50 / 50 tokens | Tokens counted as `ceil(characters / 4)` (G18); 250/40 tried in AI-3 |
| Embedding model / dimension | TBD (AI-1) / 768 | Stored per chunk |
| Vector candidates | 20 | |
| BM25 candidates / k1 / b | 20 / 1.2 / 0.75 | AI-3 |
| RRF k | 60 | AI-3 |
| Chunks sent to the model | 5 | |
| Past cases | 10 | Same unit first (G18) |
| Evidence budget | 3,000 tokens | |
| `MinVectorScore` | 0.30 (initial) | Evidence gate per chunk (G16); calibrated on the dev set in AI-3 |
| Index cache | 5 minutes | Invalidated after ingestion |
| Chat temperature | 0 | |
| Feedback comment | ≤ 500 characters | |
| Interaction retention | 90 days | |
