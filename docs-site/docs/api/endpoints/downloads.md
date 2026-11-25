# Downloads API

**Route prefix:** `/api/downloads`

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/downloads` | List batches |
| GET | `/api/downloads/{id}` | Detail |
| POST | `/api/downloads/{id}/cancel` | Cancel (**confirm**) |
| POST | `/api/downloads/{id}/retry` | Retry (**confirm**) |

Terminal-scoped download **start** lives under `POST /api/terminals/{id}/downloads`.

Start returns a `Pending` batch with the selected versions already prepared. Send `scope: "Partial"` and `partialTableDefinitionIds` to select records. The host includes their required records. The next eligible MT8, MT9 or MT44 claims the oldest pending batch for that phone after any eligible fleet schedule. The same batch ID records the wire transfer and the terminal's MT14 results. `nccSessionId` is null before pickup and identifies the resulting session afterward.

Cancel a pending batch before the phone starts receiving it. Retry creates another pending batch using the original scope. MT94 uses the separate firmware scheduler.

Start and retry reject a known installed firmware size, record type or explicitly confirmed layout conflict with HTTP 400 and a message identifying the record. The checks cover only the requested records and their dependency closure. A rejected request creates no batch; an unrelated record in a partial source set does not prevent a compatible selection. Unclassified firmware and types without verified lengths in an incomplete catalogue are not rejected merely for being unknown. The NCC pickup repeats the compatibility check on the prepared versions before sending; a historical or newly incompatible queued batch records a not-sent failure.

**UI:** `/downloads`, `/downloads/:id`

**Entities:** `DownloadBatch`, `DownloadBatchItem`
