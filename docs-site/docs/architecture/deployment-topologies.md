# Deployment topologies

Common patterns:

1. **Single VM / compose** — Postgres + API + reverse proxy; worker optional.
2. **Split** — Managed Postgres + container API + static UI on CDN.
3. **Lab** — In-memory DB for tests only — **not** production.

!!! warning "PRODUCTION GUARD REQUIRED"
    TLS termination, secrets rotation, and DB network isolation are mandatory for external exposure.
