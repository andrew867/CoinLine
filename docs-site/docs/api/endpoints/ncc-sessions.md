# NCC sessions API

**Route prefix:** `/api/ncc/sessions`

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/ncc/sessions` | List sessions and whether their sockets are bound. Use `includeArchived=true` to include archived records. |
| GET | `/api/ncc/sessions/{id}` | View terminal binding, correlation ID, and ordered frames with raw hex. |
| POST | `/api/ncc/sessions/{id}/close` | Mark the persisted session closed. |
| POST | `/api/ncc/sessions/{id}/archive` | Archive a persisted session for audit. |

The detail response includes each frame's sequence and terminal match. Use `socketBound` to check for a live socket independently of the persisted status. The console lists sessions at `/ncc-sessions`.

**Entity:** `NccSession`
