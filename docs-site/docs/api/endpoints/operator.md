# Operator / dashboard API

**Route prefix:** `/api/operator`

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/operator/dashboard` | Aggregated dashboard JSON |
| GET | `/api/operator/search` | Global search |
| GET | `/api/operator/customers/{id}/console` | Customer console |
| GET | `/api/operator/customers/{id}/timeline` | Timeline |
| GET | `/api/operator/terminals/{id}/timeline` | Terminal timeline |

**Related UI:** Dashboard `/`, GlobalSearch, customer/terminal drill-downs.

!!! note "Read-heavy"
    Dashboard aggregates are host-side — modem truth **`HARDWARE VALIDATION REQUIRED`**.
