# CoinLine Payphone Management Platform

**CoinLine** is an OEM-grade, server-hosted platform for managing **Millennium payphone** fleets: provisioning, **table distribution**, **call rating**, card/account administration (payment-adjacent, simulation-capable defaults), technician **craft** workflows, **firmware package** orchestration, protocol-aware diagnostics, and **audit** trails. Customers deploy **CoinLine Server** and **CoinLine Management Console** on their own infrastructure.

| | |
|---|---|
| **Repository** | [github.com/andrew867/CoinLine](https://github.com/andrew867/CoinLine) |
| **Documentation (MkDocs source)** | [`docs-site/docs/`](docs-site/docs/) — build with [MkDocs Material](https://squidfunk.github.io/mkdocs-material/) |
| **OpenAPI** | [`docs-site/docs/api/openapi/coinline.openapi.json`](docs-site/docs/api/openapi/coinline.openapi.json) |
| **Release** | **v0.1.0** — see [Changelog](docs-site/docs/release/changelog.md) and [Known limitations](docs-site/docs/reference/known-limitations.md) |
| **License** | [MIT](LICENSE) — Copyright (C) 2026 Andrew Green |

---

## Components

| Name | Description |
|------|-------------|
| **CoinLine Host** | Terminal-side / gateway integration surface (terminology used for field and OEM integration docs). |
| **CoinLine Server** | ASP.NET Core API, PostgreSQL (EF Core), optional worker — see `backend/` and `docker/`. |
| **CoinLine Management Console** | React + TypeScript operator SPA — see `web/`. |
| **CoinLine Field Tools** | Field validation, capture, and lab workflows (console + validation APIs). |
| **CoinLine API** | REST + OpenAPI (**CoinLine API** title in Swagger). Authenticate with API keys in production. |

---

## Features (truthful scope)

- Fleet hierarchy: customers, sites, terminals, groups; terminal events and status.
- **Table distribution**: definitions, payloads, sets, assignments, download batches (confirm-guarded risky operations).
- **DLOG** ingestion with raw payload retention and diagnostics.
- **NCC**-oriented framing libraries and capture APIs for validation workflows.
- **Rating** MVP: plans, rules, quote/authorize, call records (not asserted full production billing parity).
- **Cards** ledger and reconciliation scaffolding — **simulation-oriented defaults**; scope **PCI** independently.
- **Craft** sessions and simulated commands — live modem paths require **field validation**.
- **Firmware** package registry and jobs — live programming **gated** (`Firmware:AllowLiveFlashing`).
- **Audit** events for sensitive mutations; health/readiness/metrics hooks.

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

Entry points: [Documentation home](docs-site/docs/index.md), [Quickstart](docs-site/docs/getting-started/quickstart.md), [Server deployment](docs-site/docs/deployment/server-deployment.md), [Security overview](docs-site/docs/security/security-overview.md).

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

## Public documentation hygiene

```bash
bash tools/public-docs-sanitize-check.sh
```

See [release-checklist.md](release-checklist.md) and [Public release checklist](docs-site/docs/release/public-release-checklist.md).

---

## Contributing & support

Issues and PRs are welcome on [GitHub](https://github.com/andrew867/CoinLine).  
Support channels for production deployments are **organization-defined** — see [Support policy](docs-site/docs/release/support-policy.md).

---

## Disclaimer

CoinLine is intended for **evaluation**, **integration**, and **field validation** aligned with your OEM and compliance programs. It is **not** offered as a turnkey certified billing switch or PCI-compliant card environment without your own assessment and scope.

---

**Copyright (C) 2026 Andrew Green** — licensed under the [MIT License](LICENSE).
