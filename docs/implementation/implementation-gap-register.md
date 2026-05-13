# CoinLine implementation gap register

**Last updated:** 2026-05-02 (Pass 1 partial): Added `HostPlatform.Transport`, XMODEM core (`XmodemSender`, `XmodemCodec`, `XmodemPerfectReceiver`), unified `DlXmodemTransportAdapter` (memory-loop simulation + gated `ExecuteLiveTransferAsync`), UART/EEPROM catalogs, modem pacing/state helpers, env-bound `DlTransport` options in API. **GAP-0001 / GAP-0007 remain open** until worker-backed live job execution, transcript/events APIs, OpenAPI regeneration, and docs-site pass are completed.

**Scan scope:** `coinline/backend`, `coinline/web`, `coinline/docs-site`, `coinline/docs`, `coinline/fixtures`, `coinline/tools`, `coinline/scripts`.

**Column guide**

| Column | Meaning |
|--------|--------|
| **HW** | Hardware / field validation required before treating behavior as certified. |
| **Tranche** | Roadmap bucket from the product plan. |
| **Status** | `open`, `in_progress`, `closed`, or `deferred` (with test/issue). |

---

## Baseline (Tranche 0)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0001 | `backend/src/HostPlatform.Firmware/DlXmodemTransportAdapter.cs` | XMODEM/DL transport | **In progress:** real adapter + `HostPlatform.Transport`; memory duplex simulation exercises framing | Finish live harness docs, full negative-path matrix, transcript persistence, OpenAPI export | Firmware | Unit (`XmodemCodecTests`, `DlXmodemTransportAdapterTests`) + `FirmwareTranche8Tests` | Yes | 8 | in_progress |
| GAP-0002 | `backend/src/HostPlatform.Api/Middleware/DevOperatorMiddleware.cs` | Dev operator | TODO: production auth | OIDC/API key + roles | API | Authz tests | No | 12 | open |
| GAP-0003 | `backend/src/HostPlatform.Rating/RatingEngine.cs` | `RatingEngine` | Rules + tariff catalog (destination + time bands); SetRated/TableRated use host plan | Deterministic quotes + audit trail | Rating | Unit + `RatingTranche5Tests` | Yes | 5 | closed |
| GAP-0004 | `backend/src/HostPlatform.Infrastructure/Rating/RatingWorkflow.cs` | Segments / labels | Airtime source–aware segments + `DetailJson` | Persisted labels match quote path | Rating | Integration | Yes | 5 | closed |
| GAP-0005 | `HostPlatform.Infrastructure/Craft/CraftSimulationTransport.cs` | `ICraftSimulationTransport` | — | Injectable simulation craft path (live modem HARDWARE_VALIDATION_REQUIRED) | Craft | `CraftTranche7Tests` | Yes | 7 | closed |
| GAP-0006 | `HostPlatform.Infrastructure/Cards/CardAccountLedger.cs` | `ICardAccountLedger` | Simulation-only placeholder string | Balance adjustments + payment amounts applied in simulation; gates unchanged | Cards | `CardTranche6Tests` + `CardTranche8Tests` | Yes | 8 | closed |
| GAP-0007 | `backend/src/HostPlatform.Api/Services/FirmwareJobOrchestrator.cs` | DLA path | Simulation invokes adapter; live flash still gated | Worker lease/queue, job steps/transcripts/events, start/cancel API parity | Firmware | Job worker integration tests | Yes | 8 | open |
| GAP-0008 | `HostPlatform.Protocols.Dlog/DlogCorrelationRules.cs` | Correlation catalogue | Heuristic classifier diagnostics | Documented pairs + `fixtures/dlog/correlation_pairs.fixture.json` aligned in protocol tests | DLOG | `DlogCorrelationCatalogFixtureTests` | Yes | 9 | closed |
| GAP-0009 | `HostPlatform.Infrastructure/Dlog/DlogTransactionEngine.cs` | Session pairing | Bidirectional ingest | Bidirectional pairing (request-first + response-first ingest); `DlogTranche9Tests` | DLOG | Integration | Yes | 9 | closed |
| GAP-0010 | `web/src` (various) | Management Console | NCC Sessions list + lifecycle actions; other workflows partial | Full operator UX per workflow | UI | Playwright e2e | No | 11 | open |
| GAP-0011 | `docs-site/docs/reference/branding.md` | Support placeholders | GitHub Issues portal; email noted not published | Customer values before public launch | Docs | Review | No | 13 | closed |
| GAP-0012 | `backend/src/HostPlatform.Infrastructure/Persistence/SeedData.cs` | Table seed rows | "MVP placeholder" descriptions | Accurate catalog text | Tables | `SeedDataTableCatalogTranche10Tests` | No | 10 | closed |
| GAP-0013 | `backend/src/HostPlatform.Domain/RatingAndCards.cs` | Time bands | `DayOfWeekMask` + minute-of-day evaluated in `RatingEngine` | Field GA still needs bench clock/SKU proof | Rating | `RatingTariffCatalogQuoteTests` | Yes | 5 | closed |
| GAP-0014 | NCC / DLOG / Table / Rating APIs | OpenAPI | Controllers listed | Regenerated + reviewed per tranche | API | OpenAPI snapshot | No | 1–13 | closed |

---

## NCC (Tranche 1)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0100 | `HostPlatform.Protocols.Ncc` | `NccParseMode` | Legacy permissive parse mode enum member renamed | `DiagnosticCapture` (diagnostic/capture inspection) | NCC | Protocol unit | No | 1 | closed |
| GAP-0101 | `HostPlatform.Protocols.Ncc` | Stream parsing | `NccStreamReader` (buffer) | `NccIncrementalStreamDecoder` for chunk feeds + tests | NCC | `NccIncrementalStreamDecoder` tests | No | 1 | closed |
| GAP-0102 | `HostPlatform.Api` | REST | `frame-captures` | Also `api/ncc/captures` + `decode` / `encode` / `replay` | NCC | Integration HTTP | No | 1 | closed |
| GAP-0103 | `tools/HostPlatform.Tools.NccReplay` | CLI | Stream JSON | `decode`, `encode`, `replay`, `validate`, `split-stream` + legacy argv | NCC | Manual/CI | No | 1 | closed |
| GAP-0104 | `fixtures/ncc/**` | Fixtures | Sparse | `valid/clr_sample.*`; `malformed/crc_mismatch_min.bin`; `streams/` README | NCC | `NccMalformedFixtureTests` + protocol suite | No | 1 | closed |
| GAP-0105 | `HostPlatform.Domain/NccSession.cs` | Session entity | `NccSessionStatus` + column; `includeArchived` list; close/archive | Full lifecycle enum + persistence | NCC | `NccSessionTranche12Tests` + `NccSessionStatusTests` | No | 12 | closed |

---

## DLOG (Tranche 2)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0200 | `HostPlatform.Api` | Terminal-scoped list | — | `GET /api/terminals/{id}/dlog` mirrors filtered transactions list | DLOG | Integration HTTP | No | 2 | closed |
| GAP-0201 | `HostPlatform.Protocols.Dlog/DlogMessageTypeRegistry.cs` | Registry completeness | Broad registry | `ValidateMetadataCompleteness()` + unit tests | DLOG | `DlogMessageTypeRegistryTests` | No | 2 | closed |

---

## Table distribution (Tranche 3)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0300 | `HostPlatform.Protocols.Tables` | `TableDownloadStateMachine` | `TableDistributionPlaceholder` removed | Legal transitions + `SuggestedItemStatus` | Tables | Unit tests | Yes | 3 | closed |
| GAP-0301 | `HostPlatform.Api/Controllers/DownloadsController.cs` | Timeline | Items omit phase | `hostDownloadPhase` + name on `GET /api/downloads/{id}`; UI shows phase | Tables | `TableDistributionTranche4Tests` | Yes | 3 | closed |
| GAP-0302 | `HostPlatform.Domain/DownloadBatchItem` | Item fields | Item status | `HostDownloadPhase` + migration `20260503034129` | Tables | Migration + unit | Yes | 3 | closed |

---

## Uploads / CDR host ingestion (Tranche 4)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0400 | `HostPlatform.Infrastructure/Uploads/UploadBatchProcessor.cs` | Extraction | — | JSON array / `records` envelope / monolithic paths | Uploads | Integration `UploadTranche4Tests` | No | 4 | closed |
| GAP-0401 | `HostPlatform.Api/Controllers/UploadsController.cs` | REST | List/create/get | `POST .../ingest`, `.../reprocess?confirm`, `.../operator-review` | Uploads | Integration + OpenAPI | No | 4 | closed |
| GAP-0402 | `web/src/pages/UploadsPage.tsx`, `UploadDetail.tsx` | UI | JsonPanel | List + detail with ingest / reprocess / review | UI | `npm run build` | No | 4 | closed |
| GAP-0403 | `HostPlatform.Api/Controllers/CallRecordsController.cs` | Reconcile | — | `POST /api/call-records/{id}/reconcile` (already present) | CDR | Integration `ControllersApiTests` path | No | 4 | closed |

---

## Rating / tariff catalog (Tranche 5)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0500 | `RatingEngine` / `RatingWorkflow` | Catalog + modes | Host rules + `DestinationPrefixes` + `TimeBands` (`Tariff`); time band wins over destination when both match; `RatingAirtimeSource` on quote | Operator publishes versions + tariffs (seed + operator CRUD, Tranche 6) | Rating | Unit + `RatingTranche5Tests` | Yes | 5 | closed |

---

## Rating catalog operator CRUD (Tranche 6)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0510 | `RatePlansController` + `TariffsController` + `DestinationPrefixesController` + `TimeBandsController` | Catalog REST | POST/GET + clone | Draft-only PUT/DELETE; tariff delete blocked when referenced; `GET …/versions/{id}` returns full catalog | Rating | `RatingTranche6Tests` | No | 6 | closed |
| GAP-0511 | `web/src/pages/RatePlanDetail.tsx` | Rate plan UI | Rules only | Lists tariffs, prefixes, bands; add/save/delete on draft; clone version from selection | UI | `npm run build` | No | 6 | closed |

---

## Craft simulation transport (Tranche 7)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0520 | `HostPlatform.Infrastructure/Craft/*` + `CraftController` | Craft exec | — | `ICraftSimulationTransport` registered; controller injects simulation path | Craft | `CraftTranche7Tests` | Yes | 7 | closed |
| GAP-0521 | `HostPlatform.Craft/CraftTransportCapabilities.cs` | Transport metadata | — | Documents simulation default + live attach notice | Craft | Unit + fixture `fixtures/craft/channel_placeholder.fixture.json` | No | 7 | closed |

---

## Card ledger (Tranche 8)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0530 | `HostPlatform.Infrastructure/Cards/*` + `CardsController` | Ledger service | — | `ICardAccountLedger` applies adjustments + simulation-mode payment deltas | Cards | `CardAccountLedgerTests` + `CardTranche8Tests` | Yes | 8 | closed |
| GAP-0531 | `HostPlatform.Cards/CardLedgerCapabilities.cs` | Ledger metadata | — | Documents simulation default + live settlement notice | Cards | Unit + `fixtures/cards/ledger_simulation_placeholder.fixture.json` | No | 8 | closed |

---

## DLOG correlation (Tranche 9)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0540 | `DlogTransactionEngine` + `DlogCorrelationRules` | Pairing | Bidirectional ingest + terminal/session correlation | Bidirectional compatibility pairing + stable pair list | DLOG | `DlogTranche9Tests` + existing `ControllersApiTests.Dlog_rate_request_response_correlates` | Yes | 9 | closed |
| GAP-0541 | `DlogController` `GET …/correlation-pairs` | Operator API | — | Exposes catalogue for UI/docs | DLOG | Integration `DlogTranche9Tests.Correlation_pairs_route_matches_rules_count` | No | 9 | closed |

---

## Table seed catalog (Tranche 10)

Closes baseline **GAP-0012**: demo-seeded `TableDefinition` rows (table numbers **10**, **20**, **30**) carry operator-grade names and descriptions in `SeedData.cs`; premium **900** block rule seed JSON uses a neutral catalog note. Verification: `SeedDataTableCatalogTranche10Tests` (`GET /api/tables/definitions`).

---

## Management console (Tranche 11)

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0110 | `web/src/pages/NccSessionsPage.tsx` + `App.tsx` | NCC sessions route | List + summary + error/empty; terminal link | Replace JsonPanel with sorted `GET /api/ncc/sessions` table, deep-links, e2e | UI | Playwright `smoke.spec.ts`, `tranche11-ncc-sessions.spec.ts` | No | 11 | closed |

Contributes to baseline **GAP-0010** (still **open** until full Maximizer-parity operator workflows are covered).

---

## NCC session lifecycle (Tranche 12)

Closes **GAP-0105**: `NccSessionStatus` (`Active` / `Closed` / `Archived`) persisted on `NccSessions`; migration `20260503072310_NccSessionLifecycleStatus` + SQL backfill from `EndedAtUtc`; `GET /api/ncc/sessions?includeArchived=`; `POST …/{id}/close` and `POST …/{id}/archive`; operator `openNccSessions` counts **Active** only; demo seed sets **Active**; Management Console shows status, optional Close/Archive, “Include archived”. Tests: `NccSessionTranche12Tests`, `NccSessionStatusTests`.

---

## Branding & DLA simulation (Tranche 13)

Closes baseline **GAP-0011** (branding table — support portal and published-mail policy).

| ID | File | Symbol / area | Current | Required | Domain | Test | HW | Tranche | Status |
|----|------|-----------------|---------|----------|--------|------|----|---------|--------|
| GAP-0131 | `HostPlatform.Firmware/DlXmodemSimulationTransportAdapter.cs` + `FirmwareJobOrchestrator` | DLA sim | Registered `IDlXmodemTransportAdapter`; `dla_xmodem_transport` step succeeds | Live UART remains **GAP-0001** | Firmware | `FirmwareTranche8Tests`, `DlXmodemSimulationTransportAdapterTests` | Yes | 13 | closed |

Ordered backlog for remaining baseline gaps: **`docs/implementation/remaining-work-sequence.md`**.

---

## Deferred / compatibility validation

Items intentionally left as **diagnostics** (not silent success) until capture-backed validation: UART gap classification, rate-request correlation keys, firmware EEPROM layout, live modem pacing. These stay visible in API JSON as `HARDWARE_VALIDATION_REQUIRED` where applicable.

---

## How to close an item

1. Implement with tests listed in the row.  
2. Update **Status** to `closed` and reference PR or commit.  
3. If deferring, add a **skipped/failing test** or GitLab/GitHub issue ID in the row notes.
