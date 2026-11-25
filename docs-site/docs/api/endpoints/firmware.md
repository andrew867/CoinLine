# Firmware API

**Route prefix:** `/api/firmware`

Packages, artifacts, compatibility rules, targets, jobs:

| Area | Examples |
|------|----------|
| Policy | `GET /api/firmware/execution-policy` |
| Catalog | `GET/POST .../packages`, `GET .../packages/{id}`, artifacts POST |
| Versions | `GET/POST .../versions` |
| Revise a build's layout | `POST .../versions/{id}/table-profile/preview`, `PUT .../versions/{id}/table-profile` |
| Table layouts | `GET .../table-profiles` |
| Targets | `GET/POST .../targets` |
| Jobs | `GET/POST .../jobs`, `GET .../jobs/{id}`, `simulate`, `approve`, `cancel` |

Register a build with `label`, optional `buildId` and optional `tableProfileId` from the table layout catalog. A table set can target a registered version independently of how many record lengths are known for it. Register builds separately when their table layouts differ.

`GET /api/firmware/table-profiles` returns the known `verifiedLengths` for each profile and the subset `terminalAcknowledgedRecords` observed on OEM emulated firmware. The remaining lengths come from compiled terminal catalogues. `completeCatalogue` indicates that all provisionable message types were identified for the selected image profile. Choose that profile only for a matching build.

!!! danger "Live flashing"
    **`Firmware:AllowLiveFlashing`** default **false**. DLA/XMODEM **`HARDWARE VALIDATION REQUIRED`**.

To revise a registered build, preview with `{"tableProfileId":"mtr19-multipay-200"}`. Use explicit `null` to remove its verified profile reference. The preview returns affected counts, grouped findings, `canApply` and a `previewToken`. Apply with the same `tableProfileId` and token. Both endpoints require Firmware registry Update permission. An incompatible published targeted or currently assigned record, incompatible queued selection, active NCC session or active transfer blocks the change; draft findings and unobserved lengths are advisory. Scheduled and queued partial transfers are reviewed using their selected records and dependencies, not unrelated source records. A changed configuration or blocking finding returns HTTP 409. Apply updates only the registered firmware reference, its revision and audit history. It does not rewrite tables, reassign phones, schedule downloads or certify device operation.

**UI:** `/firmware/packages`, `/firmware/jobs`, etc.
