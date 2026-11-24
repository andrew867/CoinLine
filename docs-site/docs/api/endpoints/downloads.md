# Downloads API

**Route prefix:** `/api/downloads`

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/downloads` | List batches |
| GET | `/api/downloads/{id}` | Detail |
| POST | `/api/downloads/{id}/cancel` | Cancel (**confirm**) |
| POST | `/api/downloads/{id}/retry` | Retry (**confirm**) |

Terminal-scoped download **start** lives under `POST /api/terminals/{id}/downloads`.

**UI:** `/downloads`, `/downloads/:id`

**Entities:** `DownloadBatch`, `DownloadBatchItem`
