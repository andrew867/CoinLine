# Quickstart (Docker)

!!! note "Compose location"
    Primary compose files live under `coinline/docker/` (see [Docker Compose](../operations/docker-compose.md)).

## Prerequisites

- Docker / Docker Compose
- Ports **5432** (Postgres), **5006** (API), **5173** (optional UI via host networking)

## Typical flow

1. Start PostgreSQL (or use compose stack).
2. Apply EF migrations (from dev container or host with `dotnet ef`).
3. Run API image or `dotnet run` with connection string pointing at the DB.
4. Run web dev server or serve static build behind reverse proxy.

!!! danger "PRODUCTION GUARD REQUIRED"
    Never expose Postgres or API admin routes without TLS, API keys, and network policy.

See [Operations → Production deployment](../operations/production-deployment.md) for hardening.
