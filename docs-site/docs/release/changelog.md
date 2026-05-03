# Changelog

All notable changes to this repository are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions refer to **documentation + software artifacts** published together.

---

## [0.1.0] — 2026-05-03 {: #release-010 }

**Initial public release (v0.1.0).** Same technical scope as the prior **0.1.0-alpha** engineering drop: intended for **evaluation**, **lab demos**, and **integration validation** — **not** for production billing, certified payment processing, or unattended carrier-grade operation without a dedicated validation program.

Prior alpha notes remain applicable; treat **v0.1.0** as the first tagged **MIT-licensed** distribution on GitHub ([CoinLine](https://github.com/andrew867/CoinLine)).

### Release summary (package notes)

#### What works (at a high level)

- **Operator-facing web console** (React) for provisioning flows: customers, sites, terminals, table definitions/sets, downloads, uploads, DLOG inspection, rating tools, cards ledger UI, craft workflows, firmware job orchestration, audit browsing — backed by a **.NET 9** API and **PostgreSQL** persistence.
- **REST API** with API-key auth (configurable), rate limiting options, CORS, structured logging, health/readiness splits, optional Prometheus metrics, audit events for sensitive mutations.
- **Protocol scaffolding**: NCC-oriented framing path, DLOG ingest with **raw octet preservation**, message-type registry and diagnostics; table distribution with checksum-oriented payloads and guarded mutations (**confirm** patterns on risky operations).
- **Background worker process** that writes subsystem heartbeats for readiness coupling; API-hosted **retention telemetry** (counts only — not destructive by default).
- **Documentation site** (`docs-site/`) with operator, API, protocol, security, and field-validation guidance; **strict MkDocs** build supported.
- **Automated tests** (backend unit/integration, frontend tests, Playwright where configured) — they increase confidence in **software behavior**, not **modem/PSTN/firmware** correctness.

#### Simulation-only (defaults or design)

- **Card payments / ledger**: simulation-oriented defaults (`CardPayments:SimulationMode`); physical card writes disabled or simulated; reconciliation flows are **lab scaffolding**, not a certified acquirer integration.
- **Craft**: no live modem execution path for craft commands in this drop — treat as **workflow + persistence** unless extended.
- **Firmware jobs**: orchestration and safety gates exist; **live flashing** remains behind **`Firmware:AllowLiveFlashing`** (default **false**) and is **not** certified here.
- **Demo / seed data**: optional seed can populate sample tenants — **not** representative of a hardened production tenant model.

#### Requires field and device validation

- **DLOG and NCC wire semantics** on real terminals and transports — classifiers and registries are aligned to **OEM compatibility specifications** and **supported terminal firmware versions**; unknown message types and line timing must be confirmed in the **customer lab**.
- **Set/table-rated rating modes** and **prefix/tariff edge cases** — MVP rating may deny or constrain until validated against **supported payphone models** and your approved test matrices.
- **Download completion on the terminal** — the host records download intent; end-to-end terminal acknowledgment **must** be proven per deployment.
- **Any path touching PSTN, modem, UART, or field programming** — requires **field validation with supported hardware** before operational claims.

#### Not production-ready (do not misread CI green as “go live”)

- **Production billing parity** for rating and call records.
- **PCI DSS production** cardholder-data environments using this stack without an independent scope assessment and upstream controls.
- **Carrier-grade availability, DR, and multi-region** — docs describe patterns; this alpha is not a compliance-certified appliance.
- **Static OpenAPI** as the sole contract — the committed export may omit minimal routes (`/ready`, `/health/ready`, `/metrics`); integrate against **live Swagger** or tests.

#### Security warnings

- **API keys** are shared-secret HTTP headers — protect at rest, rotate, and terminate TLS at the edge; **never** expose the API anonymously to the internet.
- **Development auth modes** must not ship to production; production requires explicit **`Security:Mode`** and key configuration (see configuration docs).
- **Raw payloads** (DLOG, tables, cards) may contain sensitive traffic or account metadata — classify, restrict access, and review **audit** and **export** paths (including DLOG replay export).
- **Payment-adjacent APIs** are **not** a substitute for a PCI-scoped payment application; token references and masking discipline are **necessary but not sufficient**.

#### Deployment warnings

- **Secrets**: use environment or secret manager integration — do not commit connection strings or API keys.
- **PostgreSQL**: apply migrations deliberately; back up before upgrade; validate restore.
- **Worker + readiness**: if worker heartbeat gating is enabled, a stopped worker can fail readiness — design probes and process supervision accordingly.
- **CORS and origins**: explicit configuration required for browser clients; default deny when origins unset.
- **Seed data**: disable demo seed for strict environments.

#### Recommended demo workflow (lab)

1. Bring up PostgreSQL and apply EF migrations; run **API** and **Web** per `README.md`.
2. Use **Swagger** (`/swagger`) with a configured API key; confirm **health** vs **ready** behavior matches your ops expectations.
3. Walk the operator UI: create customer → site → terminal; inspect **simulation-state** for cards; review **audit** after mutations.
4. Ingest a **small** DLOG sample or fixture-driven path; verify **raw payload** retention and diagnostics — **do not** infer field certification from UI alone.
5. Run **`mkdocs build --strict`** on `docs-site/` before publishing docs artifacts.

#### Recommended next engineering tranche (priority-shaped, not a promise)

1. **Field validation program**: modem/UART evidence capture, terminal ACK confirmation, and updates to the **compatibility matrix** from validated deployments.
2. **OpenAPI completeness** for minimal routes or documented codegen exclusions; contract tests for probes.
3. **Rating UAT lock**: align `docs/uat/` matrices with engine behavior; tighten diagnostics for deny reasons.
4. **PCI scope boundary**: separate pure ops tenants from any CHD-touching integration; harden redaction and retention.
5. **Operational hardening**: backup/restore drills, runbooks for worker/API skew, secret rotation.

### Added

- Initial packaged public documentation site and repository README alignment for **0.1.0-alpha** release notes.

### Documentation

- Release package notes (this section), support policy, known limitations, compatibility validation index, and checklist updates for the alpha.

---

## [Unreleased]

### Added
### Changed
### Fixed
### Security

---

Adopt Keep a Changelog sections as appropriate — link release tags to Git SHAs.
