# API overview

- **Base path:** `/api`
- **Docs:** Static OpenAPI 3 at [`openapi/coinline.openapi.json`](openapi/coinline.openapi.json) — generated from the ASP.NET Core app (**controller routes**; see caveat below).
- **Interactive:** Swagger UI at `/swagger` when API runs locally.

!!! warning "Field validation"
    HTTP JSON responses **do not** certify modem timing, EEPROM programming, or PSTN tariff accuracy without **device validation** on your lines and supported terminals.

## Coverage vs narrative docs

| Surface | OpenAPI | Narrative |
|---------|---------|-----------|
| Controller routes (`Controllers/*.cs`) | Intended — regenerate export before release | One page per controller area under [Endpoints](endpoints/customers.md) … [hardware-validation](endpoints/hardware-validation.md) |
| Minimal endpoints (`Program.cs`: `/health`, `/health/live`, `/ready`, `/health/ready`, conditional `/metrics`) | **Often incomplete** in static JSON — **always** verify live `/swagger` | [Health & readiness](endpoints/health.md) |

**Cards** APIs span two narrative pages ([card products](endpoints/card-products.md), [card accounts](endpoints/card-accounts.md)) but share **`CardsController`** (`/api/cards/*`).
