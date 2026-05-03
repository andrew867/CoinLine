# Millennium Host Platform — public documentation site

Built with **MkDocs Material**. Source lives under `docs/`; built output goes to `site/` (gitignored).

## Prerequisites

- Python 3.10+ with `pip`
- .NET SDK 9+ (for OpenAPI export only)

## Install

```bash
cd docs-site
pip install -r requirements.txt
```

## Serve locally

```bash
mkdocs serve -f mkdocs.yml
```

Open **http://127.0.0.1:8000**.

## Build (strict — CI)

```bash
mkdocs build -f mkdocs.yml -d site --strict
```

Or `npm run docs:build` after `pip install`.

## OpenAPI artifacts

Generated files (re-run after API changes):

- `docs/api/openapi/host-platform.openapi.json`
- `docs/api/openapi/host-platform.openapi.yaml`

Export (from repo root or `docs-site`):

```bash
bash docs-site/scripts/export-openapi.sh
```

Windows:

```powershell
docs-site\scripts\export-openapi.ps1
```

Requires `DOTNET_ENVIRONMENT=Testing` so `swagger tofile` does not connect to PostgreSQL (see script).

## Relationship to `docs/` at repository root

Repository protocol and UAT markdown under `docs/protocols/`, `docs/uat/`, etc. remain **authoritative protocol sources**. This site summarizes implementation boundaries and links into those paths where relevant.
