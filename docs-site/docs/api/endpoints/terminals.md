# Terminals API

**Route prefix:** `/api/terminals`

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/terminals` | List |
| POST | `/api/terminals` | Create |
| GET | `/api/terminals/{id}` | Detail |
| PUT | `/api/terminals/{id}` | Update |
| GET | `/api/terminals/{id}/events` | Events |
| POST | `/api/terminals/{id}/status` | Status |
| GET | `/api/terminals/{id}/table-assignment` | Assignment |
| POST | `/api/terminals/{id}/table-assignment` | Assign (confirm) |
| POST | `/api/terminals/{id}/table-assignment/rollback` | Rollback (confirm) |
| POST | `/api/terminals/{id}/downloads` | Start download (idempotency) |
| POST | `/api/terminals/{id}/firmware-jobs` | Create firmware job |
| GET | `/api/terminals/{id}/diagnostics` | Diagnostics |
| POST | `/api/terminals/{id}/diagnostics/snapshots` | Snapshot |
| POST | `/api/terminals/{id}/request-cdr-upload` | Request CDR upload |
| POST | `/api/terminals/{id}/request-table-reload` | Request table reload |

**Confirm flags:** several POST routes require `?confirm=true` — see Swagger.

**UI:** `/terminals`, `/terminals/:id`

**Entity:** `Terminal`, related downloads/jobs
