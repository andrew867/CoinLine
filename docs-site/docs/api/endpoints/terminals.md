# Terminals API

**Route prefix:** `/api/terminals`

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/terminals` | List |
| POST | `/api/terminals` | Create |
| GET | `/api/terminals/{id}` | Detail |
| PUT | `/api/terminals/{id}` | Update |
| GET | `/api/terminals/families` | List terminal family IDs, release names, variants and Maximizer type numbers |
| PUT | `/api/terminals/{id}/family` | Set `terminalFamilyId` or clear it with null |
| PUT | `/api/terminals/{id}/firmware` | Register `firmwareVersionId` for the installed build or use null for unknown |
| GET | `/api/terminals/{id}/groups` | Read current memberships and groups available to this phone |
| PUT | `/api/terminals/{id}/groups` | Save up to 40 group IDs with the retrieved revision. The first ID is the primary group. |
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

Create accepts an optional `terminalFamilyId`. The terminal list accepts the same field as a query filter and includes the family label. A phone can have a terminal family and a separately registered firmware build. Wire settings such as card-only or Multi-pay mode are configured in MT26.

**Confirm flags:** several POST routes require `?confirm=true` — see Swagger.

**UI:** `/terminals`, `/terminals/:id`

**Entity:** `Terminal`, related downloads/jobs
