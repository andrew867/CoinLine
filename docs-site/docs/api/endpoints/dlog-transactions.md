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

The Terminal status screen also uses `GET /api/field-events/phone-options` for minimal phone identity and contact information, and `GET /api/field-events/phones/{terminalId}/current-status` for the latest valid inbound MT10 from that phone's matched NCC session. Both require Terminal status Read permission. The latter returns the retained report ID, host capture time, reported serial number, named asserted and non-asserted flags, unmapped bit count and the newest known report time. If a later report is malformed it is identified separately; no valid report returns null status groups. It does not return the entire raw payload or infer the phone's local timestamp. Opening the raw source still requires Message log Read permission.

`statusProfile` identifies the current registered status interpretation (`Earlier`, `Later`, or `Unknown`). `rawStatusHex` contains only the five status-mask bytes. Each group has `rawMask`, the named flags with their `statusIndex`, and asserted `unresolvedFlags` containing `statusIndex`, the byte-local `mask`, and a qualified description. Status interpretation for a retained matched inbound report uses the current registered profile, not an independently established historical version. An unbound or length-only profile is interpreted conservatively as unknown. `GET /api/dlog/transactions/{id}` returns the same decoded interpretation as `terminalStatus` for valid MT10 without rewriting the original `decodedJson` or raw record. The field-event list exposes the profile used for a status summary.

!!! warning "Replay export"
    `POST /api/dlog/replay` requires `?confirm=true` or JSON `confirmExport: true` — **audit** `dlog` / `replay_export`.

**UI:** `/dlog`, `/dlog/:id`, `/dlog/replay-debug`

**Entity:** `DlogTransaction`, `DlogParseDiagnostic`
