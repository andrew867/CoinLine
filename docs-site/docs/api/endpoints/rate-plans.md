# Rate plans API

**Route prefix:** `/api/rate-plans`

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/rate-plans` | List |
| GET | `/api/rate-plans/{id}` | Detail |
| POST | `/api/rate-plans` | Create plan |
| POST | `/api/rate-plans/{id}/versions` | New version |
| POST | `/api/rate-plans/{id}/publish` | Publish (**confirm** query/body) |
| GET | `/api/rate-plans/{planId}/versions/{versionId}` | Version detail |
| POST | `/api/rate-plans/{planId}/versions/{versionId}/rules` | Add rule row |

!!! warning "MVP rating"
    Published snapshot drives host quotes — **not** asserted parity with every firmware tariff table.

**Related:** [Rate rules](rate-rules.md), [Rating quote/authorize](rating.md)

**UI:** `/rate-plans`, `/rate-plans/:id`

**Entities:** `RatePlan`, `RatePlanVersion`, `RateRule`, `DestinationPrefix`, `TimeBand`, `Tariff`
