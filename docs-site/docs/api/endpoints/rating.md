# Rating API (quote & authorize)

**Controller:** `RatingOperationsController` — **`/api/rating`**

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/rating/quote` | MVP tariff quote |
| POST | `/api/rating/authorize` | Authorization decision |

!!! warning "HARDWARE VALIDATION REQUIRED"
    Set/table-rated modes and unknown prefixes — see `RatingEngine` diagnostics.

**Related:** [Rate plans](rate-plans.md), [Rate rules](rate-rules.md), [Number classes](number-classes.md)

**UI:** `/rating-quote`
