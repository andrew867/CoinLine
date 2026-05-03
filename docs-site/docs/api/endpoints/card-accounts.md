# Card accounts & ledger API

**Route prefix:** `/api/cards`

Major routes:

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/cards/simulation-state` | Banner JSON |
| GET/POST | `/api/cards/accounts` | Accounts |
| GET | `/api/cards/accounts/{id}` | Detail |
| GET | `/api/cards/accounts/{id}/timeline` | Timeline |
| POST | `/api/cards/accounts/{id}/adjust-balance` | Reason required |
| POST | `/api/cards/read-events` | Simulated reads |
| POST | `/api/cards/transactions` | Create txn |
| GET | `/api/cards/transactions` | List |
| GET/POST | `/api/cards/reconciliation-batches` | Batches |
| POST | `/api/cards/reconciliation-batches/{id}/post` etc. | State transitions (**confirm**) |
| POST | `/api/cards/write-events` | **SIMULATION ONLY** |

!!! danger "PCI boundary"
    Never send full track/PAN/CVV through these APIs — token references only.

**UI:** `/card-accounts`, `/payment-transactions`, `/card-reconciliation`
