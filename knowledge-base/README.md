# Knowledge base

The equipment manuals the technician assistant (AI-01) cites. **Every document here is synthetic**, written for the
SarawakBizOps demonstration: the brands, models, procedures, values and part numbers are invented to match the demo
data (`DemoSeeder`), and are not real manufacturer guidance.

Documents are code (docs/ai/decisions.md G8): change them by pull request. Each `*.md` file (except this README)
starts with front matter — `slug`, `title`, `brand`, `models`, `version`, and optionally `supersedes` (another
document's slug). The API ingests the folder at startup when `Ai:IngestOnStartup` is on, or with
`dotnet SarawakBizOps.Api.dll ingest-kb`; unchanged files are skipped.

| File | Models | Version | Why it is here |
|---|---|---|---|
| `norvik-nc-chillers.md` | Norvik NC-450, NC-300 | 3.1 | One manual for several models (G3); E14 = high head pressure; long troubleshooting chapter |
| `hartmann-hc-compressors.md` | Hartmann HC-75, HC-55 | 2.5 | E14 means something else here (motor overload): tests the equipment filter (G18); long alarm chapter |
| `kestrel-kb-10t-boiler-v1.md` | Kestrel KB-10T | 1.2 | Superseded by v2: must never be retrieved |
| `kestrel-kb-10t-boiler-v2.md` | Kestrel KB-10T | 2.1 | `supersedes` v1; the manhole gasket procedure changed; long troubleshooting chapter |
| `tanjung-tl-forklifts.md` | Tanjung TL-35D, TL-25E | 1.5 | Sections in Bahasa Malaysia as well as English |
| `hartmann-hp-pumps.md` | Hartmann HP-150, HP-100, HP-80 | 1.1 | **Evaluation fixture:** its "Installer notes" section is a prompt-injection attempt |

The three manuals marked "long" have fault sections of 450–900 tokens, so a section is cut into several chunks and
the AI-3 chunk-size experiment has something to measure: 69 chunks at 250/40 against 57 at 400/50, with 12 and 8
sections split into more than one chunk. The other manuals are short on purpose: every section is a single chunk.

Error code E14 appears in four documents with four meanings; only the equipment filter tells them apart.

Seeded equipment with work orders but **no** manual, to test the History and General path (G18): the Tanjung TF-12
bottle filling line and the Hartmann HB-900 band saw.
