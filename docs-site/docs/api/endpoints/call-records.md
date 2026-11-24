# Call records API

**Route prefix:** `/api/call-records`

| Method | Path |
|--------|------|
| GET | `/api/call-records` |
| GET | `/api/call-records/{id}` |
| POST | `/api/call-records` |
| POST | `/api/call-records/{id}/reconcile` (**confirm**) |

A firmware type 63 rating request creates a record for the matched terminal. The detail endpoint includes `requestRawHex`, `responseRawHex`, and the stored rating results with segments and diagnostics. The session correlation ID ties the exchange to its audit event.

**UI:** `/call-records`, `/call-records/:id`

**Entities:** `CallRecord`, `RatingResult`, segments/diagnostics
