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
| POST | `/api/cards/read-events` | Minimal read observation: optional last four digits, card type and result |
| POST | `/api/cards/transactions` | Record a ledger amount and reconciliation status; no opaque details |
| GET | `/api/cards/transactions` | List recorded payments; source indicates a matched synthetic call-detail posting or unclassified entry |
| GET | `/api/cards/authorization-attempts` | Latest 500 redacted authorization decisions or historical unknown observations; optional `terminalId` filter, card payments read permission |
| GET/POST | `/api/cards/reconciliation-batches` | Batches |
| POST | `/api/cards/reconciliation-batches/{id}/post` etc. | State transitions (**confirm**) |
| POST | `/api/cards/write-events` | **SIMULATION ONLY**; optional last four digits and card type |

!!! danger "PCI boundary"
    Never send full track/PAN/CVV through these APIs — token references only.

Authorization history never returns raw requests, processor references or arbitrary audit JSON. Approval is not a payment transaction.

New authorization observations include `requestBytes` and, for validated immediate-clear bundles, `bundledCallRecords` (zero through two). These are structural metadata only. Older observations may not have them; malformed suffixes have no validated record count. The history never contains card or trailing call-record bytes.

Read results are `0` (unknown), `1` (succeeded), or `2` (failed). Supply `panLast4` only when it contains exactly four ASCII digits; the console displays a masked fragment. If a linked account already has a valid last-four fragment, the host can use it. Old clients may still send legacy opaque fields, but the host ignores them. A write record reports the simulation disposition, not physical card programming.

The card-data migration removes legacy read/write payload and free-text operation columns, clears untrusted manual transaction details and raw payloads, and redacts historical read/write audit metadata. It preserves ledger amounts, reconciliation states, and matching host-generated settlement evidence. It does not erase historical backups or physical database storage; handle those separately under the deployment retention policy.

**UI:** `/card-accounts`, `/payment-transactions`, `/card-reconciliation`
