# Health & readiness API

Minimal endpoints mapped in `Program.cs` (not standard controller actions):

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/health` | Quick healthy JSON |
| GET | `/health/live` | Liveness |
| GET | `/ready` | Alias mapped to readiness checks |
| GET | `/health/ready` | DB + payload storage + worker heartbeat checks |

**Metrics:** `GET /metrics` when **Observability:MetricsEnabled** is true (restrict at proxy).

!!! warning "OpenAPI coverage gap"
    Generated **`coinline.openapi.json` currently includes `/health` and `/health/live` only.** Paths **`/ready`**, **`/health/ready`**, and **`/metrics`** are **implemented** but may be **absent** from the static OpenAPI export — verify live Swagger when integrating probes. Treat this spec as **incomplete for minimal endpoints** until Swashbuckle picks up top-level routes.

**Auth:** Confirm `ApiKeyGateMiddleware` exempt paths include `/health*`, `/ready*`, `/metrics` for your deployment — see `Program.cs` / middleware implementation.
