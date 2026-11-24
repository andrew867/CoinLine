# Downloads API

**Route prefix:** `/api/downloads`

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/downloads` | List batches |
| GET | `/api/downloads/{id}` | Detail |
| POST | `/api/downloads/{id}/cancel` | Cancel (**confirm**) |
| POST | `/api/downloads/{id}/retry` | Retry (**confirm**) |

Terminal-scoped download **start** lives under `POST /api/terminals/{id}/downloads`.

The NCC firmware listener also creates a batch when a matched terminal requests its assigned published tables. A batch detail includes the item status, `hostDownloadPhaseName`, `lastAckStatus`, and any error detail. Each complete record waits for its type 14 acknowledgement. A successful firmware batch also records the final end-of-data acknowledgement.

**UI:** `/downloads`, `/downloads/:id`

**Entities:** `DownloadBatch`, `DownloadBatchItem`
