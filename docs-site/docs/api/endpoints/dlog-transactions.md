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

A recognized terminal report includes a `fieldEvent` object in its transaction detail. Its title, category and summary match Terminal status. MT7 alarm reports and MT16 acknowledgements include `alarmNumber` when their bodies can be decoded. A valid MT7 phone clock is available as `phoneTime`, a wall-clock value without a timezone. `capturedAtUtc` records when the host captured the message.

!!! warning "Replay export"
    `POST /api/dlog/replay` requires `?confirm=true` or JSON `confirmExport: true` — **audit** `dlog` / `replay_export`.

**UI:** `/dlog`, `/dlog/:id`, `/dlog/replay-debug`

**Entity:** `DlogTransaction`, `DlogParseDiagnostic`
