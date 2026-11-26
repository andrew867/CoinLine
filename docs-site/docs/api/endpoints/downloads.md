# Downloads API

**Route prefix:** `/api/downloads`

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/downloads` | List batches |
| GET | `/api/downloads/{id}` | Detail |
| POST | `/api/downloads/{id}/cancel` | Cancel (**confirm**) |
| POST | `/api/downloads/{id}/retry` | Retry (**confirm**) |
| GET | `/api/downloads/preparation/options?query=...` | Minimal phone choices for an operator with Downloads Add permission |
| POST | `/api/downloads/preparation/preview` | Review 1 to 40 distinct phones and their current assigned data sets |
| POST | `/api/downloads/preparation` | Confirm the unchanged preview and prepare or schedule each set |

Terminal-scoped download **start** lives under `POST /api/terminals/{id}/downloads`.

The detail response includes `tableNumber` on each timeline item, and `sessionEvidence` when a retained NCC session is linked to the same phone. The evidence object contains the numeric session `status`, `startedAtUtc`, nullable `endedAtUtc`, `terminalClearObservedAtUtc`, `terminalClearSequence`, `releaseOutcome` and `releaseElapsedMilliseconds`. The latter two are nullable and are available when a corresponding retained terminal-clear release audit supplies recognized metadata. Release outcomes are `peer_eof`, `deadline`, `peer_error` or `shutdown`. No raw NCC frame or raw release audit is returned by this object. Absence of a retained observation is not proof that a terminal failed to clear. Batch completion, record acknowledgement and terminal service verification are separate facts.

Start returns a `Pending` batch with the selected versions already prepared. Send `scope: "Partial"` and `partialTableDefinitionIds` to select records. The host includes their required records. The next eligible MT8, MT9 or ordinary MT44 claims the oldest pending batch for that phone after any eligible fleet schedule. The same batch ID records the wire transfer and the terminal's MT14 results. `nccSessionId` is null before pickup and identifies the resulting session afterward.

An MT44 installation or memory-loss request instead delivers the full published assignment. Only after successful completion, an older partial request whose exact versions were all acknowledged can be cancelled as superseded. Its original history records the full batch ID but has no fabricated MT14s. Uncovered versions and future scheduled selections remain pending.

Cancel a pending batch before the phone starts receiving it. Retry creates another pending batch using the original scope. MT94 uses the separate firmware scheduler.

Start and retry reject a known installed firmware size, record type or explicitly confirmed layout conflict with HTTP 400 and a message identifying the record. The checks cover only the requested records and their dependency closure. A rejected request creates no batch; an unrelated record in a partial source set does not prevent a compatible selection. Unclassified firmware and types without verified lengths in an incomplete catalogue are not rejected merely for being unknown. The NCC pickup repeats the compatibility check on the prepared versions before sending; a historical or newly incompatible queued batch records a not-sent failure.

For list preparation, submit `terminalIds` and optionally `availableAtUtc` (omit or use null for immediate preparation). The preview returns the assigned set, record versions, eligibility and `previewToken` for the exact list and time. Submit the same request with that token to confirm. All selected phones must be eligible. A stale review or conflicting concurrent work returns HTTP 409 and does not prepare a partial list. The result contains each phone's schedule ID and immediate batch ID if prepared now. The existing scheduler prepares future rows when due; the published set remains pinned to the schedule, not the phone's subsequent assignment. This workflow uses Downloads Add permission and does not mutate table assignments.

**UI:** `/downloads`, `/downloads/:id`

**Entities:** `DownloadBatch`, `DownloadBatchItem`
