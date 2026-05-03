# DLOG transactions API

**Route prefix:** `/api/dlog`

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/dlog/message-types` | Registry |
| GET | `/api/dlog/messages` | Legacy alias |
| GET | `/api/dlog/transactions` | List + filters |
| GET | `/api/dlog/transactions/{id}` | Detail + diagnostics |
| GET | `/api/dlog/transactions/{id}/payload` | Raw octets file |
| POST | `/api/dlog/transactions/ingest` | Ingest raw hex |
| POST | `/api/dlog/replay` | Export concatenated bytes (**confirm**) |
| POST | `/api/dlog/decode` | Decode-only (no persist) |

!!! warning "Replay export"
    `POST /api/dlog/replay` requires `?confirm=true` or JSON `confirmExport: true` — **audit** `dlog` / `replay_export`.

**UI:** `/dlog`, `/dlog/:id`, `/dlog/replay-debug`

**Entity:** `DlogTransaction`, `DlogParseDiagnostic`
