<div align="center">

# CoinLine Payphone Management Platform

**Server-hosted management plane for Millennium payphone fleets** — provisioning, table distribution, call rating, card/account administration, technician craft workflows, firmware orchestration, protocol-aware diagnostics, and audit trails.

[![Backend](https://github.com/andrew867/CoinLine/actions/workflows/backend.yml/badge.svg)](https://github.com/andrew867/CoinLine/actions/workflows/backend.yml)
[![Web](https://github.com/andrew867/CoinLine/actions/workflows/web.yml/badge.svg)](https://github.com/andrew867/CoinLine/actions/workflows/web.yml)
[![Docs](https://github.com/andrew867/CoinLine/actions/workflows/docs.yml/badge.svg)](https://github.com/andrew867/CoinLine/actions/workflows/docs.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Node](https://img.shields.io/badge/Node-20%2B-339933?logo=node.js&logoColor=white)](https://nodejs.org/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16%2B-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![OpenAPI](https://img.shields.io/badge/OpenAPI-3.0-6BA539?logo=openapiinitiative&logoColor=white)](docs-site/docs/api/openapi/coinline.openapi.json)
[![Release](https://img.shields.io/badge/release-v0.2.0-0aa)](docs-site/docs/release/changelog.md)
[![Status: pre-production](https://img.shields.io/badge/status-pre--production-orange)](docs-site/docs/reference/known-limitations.md)

Companion project: **[CoinLine Terminal Emulator](https://github.com/andrew867/CoinLine-emu)** — host-side functional emulator for the same payphone family, used for lab and integration validation.

</div>

---

## What this is

**CoinLine** is the **server-side** counterpart to the terminal emulator. It runs on customer infrastructure as **CoinLine Server** (ASP.NET Core API + PostgreSQL + optional worker) plus the **CoinLine Management Console** (React + TypeScript). It is built for OEM and integrator workflows: provisioning, table downloads, rating, cards, craft, firmware orchestration, DLOG/NCC inspection, and audit.

| | |
|---|---|
| **Repository** | [github.com/andrew867/CoinLine](https://github.com/andrew867/CoinLine) |
| **Documentation (MkDocs source)** | [`docs-site/docs/`](docs-site/docs/) — build with [MkDocs Material](https://squidfunk.github.io/mkdocs-material/) |
| **OpenAPI** | [`docs-site/docs/api/openapi/coinline.openapi.json`](docs-site/docs/api/openapi/coinline.openapi.json) |
| **Release** | **v0.2.0** — see [Changelog](docs-site/docs/release/changelog.md) and [Known limitations](docs-site/docs/reference/known-limitations.md) |
| **License** | [MIT](LICENSE) — Copyright (C) 2026 Andrew Green |

---

## Architecture at a glance

```
                ┌──────────────────────────────┐
                │  CoinLine Management Console │   React + TypeScript SPA
                │  (web/)                      │
                └────────────┬─────────────────┘
                             │ HTTPS (API key auth, CORS)
                             ▼
┌───────────────────────────────────────────────────────────┐
│                       CoinLine Server                     │
│                       (backend/)                          │
│                                                           │
│  ASP.NET Core 9 API ── OpenAPI / Swagger                  │
│   ├── Provisioning · Tables · Downloads · DLOG · NCC      │
│   ├── Rating · Cards · Craft · Firmware · Audit · Health  │
│   └── Background worker (heartbeats, retention telemetry) │
└────────────┬──────────────────────────────────────────────┘
             │ EF Core (Npgsql)
             ▼
      ┌────────────────┐
      │  PostgreSQL 16 │   migrations, raw payload retention, audit log
      └────────────────┘
```

---

## Components

| Name | Description |
|------|-------------|
| **CoinLine Host** | Terminal-side / gateway integration surface (terminology used for field and OEM integration docs). |
| **CoinLine Server** | ASP.NET Core API, PostgreSQL (EF Core), optional worker — see [`backend/`](backend/) and [`docker/`](docker/). |
| **CoinLine Management Console** | React + TypeScript operator SPA — see [`web/`](web/). |
| **CoinLine Field Tools** | Field validation, capture, and lab workflows (console + validation APIs). |
| **CoinLine API** | REST + OpenAPI (**CoinLine API** title in Swagger). Authenticate with API keys in production. |

---

## Features (truthful scope)

- **Fleet hierarchy** — customers, sites, terminals, groups; terminal events and status.
- **Table distribution** — definitions, payloads, sets, assignments, download batches (confirm-guarded risky operations).
- **DLOG** ingestion with **raw payload retention** and diagnostics.
- **NCC**-oriented framing libraries and capture APIs for validation workflows.
- **Rating MVP** — plans, rules, quote / authorize, call records. Not asserted as full production billing parity.
- **Cards** ledger and reconciliation scaffolding — **simulation-oriented defaults**; scope **PCI** independently.
- **Craft** sessions and simulated commands — live modem paths require **field validation**.
- **Firmware** package registry and jobs — live programming **gated** behind `Firmware:AllowLiveFlashing`.
- **Audit** events for sensitive mutations; health/readiness/metrics hooks for ops integration.

---

## Project status — honest version

This release is for **evaluation, lab demos, integration validation, and OEM customer pilots**. It is **not** a turnkey carrier-grade billing switch or a PCI-compliant payment environment.

What works end-to-end today:

- API, console, OpenAPI export, MkDocs strict build, Docker Compose dev stack.
- Provisioning, table CRUD + download orchestration, DLOG ingest, NCC decode/capture, audit, health/readiness.
- Rating MVP with quote/authorize flows; cards ledger UI in simulation mode; craft and firmware workflow scaffolding with gates.

What is intentionally simulated or gated:

- **Card payments** default to simulation; physical card writes are disabled or simulated.
- **Craft** has no live modem execution path enabled — workflow + persistence only unless extended.
- **Firmware live flashing** is off by default and requires explicit configuration and field validation.

What still requires **field validation with real hardware**:

- DLOG / NCC wire semantics on supported terminal firmware.
- Set / table-rated rating against supported payphone models.
- End-to-end download acknowledgment on the terminal.
- Anything touching PSTN, modem, UART, or programming pins.

See [Known limitations](docs-site/docs/reference/known-limitations.md), [Open questions](docs-site/docs/reference/open-questions.md), and [Hardware validation required](docs-site/docs/protocols/hardware-validation-required.md).

---

## Requirements

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) and npm (for the Management Console)
- [PostgreSQL 16+](https://www.postgresql.org/) (or use Docker)

Optional: Docker / Docker Compose for API + database (+ worker profile).

---

## Quickstart (development)

### 1. PostgreSQL

```bash
docker run --name coinline-pg -e POSTGRES_PASSWORD=coinline -e POSTGRES_USER=coinline \
  -e POSTGRES_DB=coinline -p 5432:5432 -d postgres:16-alpine
```

### 2. CoinLine Server

From the **repository root**:

```bash
cd backend
dotnet tool restore
dotnet ef database update --project src/HostPlatform.Infrastructure --startup-project src/HostPlatform.Api
dotnet run --project src/HostPlatform.Api --launch-profile http
```

- HTTP API / Swagger UI: **http://localhost:5006/swagger**

Configure API keys for non-development environments — see [`.env.example`](.env.example) (`COINLINE_API_KEYS`).

### 3. CoinLine Management Console

```bash
cd web
npm install
npm run dev
```

- Console (dev): **http://localhost:5173** — proxies `/api` to the API.

### 4. Docker Compose (alternative)

```bash
cd docker
docker compose up --build
```

---

## Build & test

```bash
cd backend && dotnet build && dotnet test
cd ../web && npm ci && npm run build && npm run test
```

Playwright E2E (optional):

```bash
cd web && npx playwright install --with-deps && npm run e2e
```

---

## Documentation site

```bash
cd docs-site
pip install -r requirements.txt
mkdocs serve -f mkdocs.yml
```

Strict build (recommended before release):

```bash
cd docs-site && mkdocs build -f mkdocs.yml -d site --strict
```

Entry points: [Documentation home](docs-site/docs/index.md) · [Quickstart](docs-site/docs/getting-started/quickstart.md) · [Server deployment](docs-site/docs/deployment/server-deployment.md) · [Security overview](docs-site/docs/security/security-overview.md).

---

## OpenAPI export

Regenerate committed artifacts after API contract changes:

**Windows (repository root):**

```powershell
powershell -ExecutionPolicy Bypass -File docs-site/scripts/export-openapi.ps1
```

**Linux / macOS:**

```bash
bash docs-site/scripts/export-openapi.sh
```

Outputs: `docs-site/docs/api/openapi/coinline.openapi.{json,yaml}`

---

## Configuration

- Environment variables: [reference — Environment variables](docs-site/docs/reference/environment-variables.md) and [`.env.example`](.env.example).
- Product naming / support placeholders: [reference — Branding](docs-site/docs/reference/branding.md).

---

## How to help

Issues and PRs are welcome — this project is most useful when integrators and lab operators share what they hit on real hardware:

- **Field validation reports.** Open an issue with the supported terminal firmware version, the workflow you ran, what worked, and what did not. Compatibility-matrix PRs against `docs-site/docs/field-validation/` are especially welcome.
- **OpenAPI gaps.** If the committed export disagrees with live `/swagger`, file an issue with both excerpts.
- **Rating edge cases.** Fixture + expected-output PRs under `fixtures/` are the fastest way to harden the rating engine.
- **DLOG / NCC samples.** Anonymized samples that exercise unknown message types or unusual framing are gold for the diagnostics path.
- **Documentation polish.** MkDocs strict build must stay green; PRs that close TODOs in `docs-site/docs/` are very welcome.

For protocol and hardware notes please keep contributions **technical and generic** — describe signals, ports, framing, and observed behavior rather than specific customer sites or proprietary firmware artifacts.

---

## Contributing & support

Issues and PRs: [GitHub](https://github.com/andrew867/CoinLine).  
Support channels for production deployments are **organization-defined** — see [Support policy](docs-site/docs/release/support-policy.md).

---

## Companion project

- **[CoinLine Terminal Emulator](https://github.com/andrew867/CoinLine-emu)** — MAME-based functional emulator for the same payphone family. Useful for protocol bring-up, host integration against a deterministic terminal, and reproducing DLOG / NCC behavior without physical hardware.

---

## Disclaimer

CoinLine is intended for **evaluation**, **integration**, and **field validation** aligned with your OEM and compliance programs. It is **not** offered as a turnkey certified billing switch or a PCI-compliant card environment without your own assessment and scope.

---

**Copyright (C) 2026 Andrew Green** — licensed under the [MIT License](LICENSE).
