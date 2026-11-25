# Firmware API

**Route prefix:** `/api/firmware`

Packages, artifacts, compatibility rules, targets, jobs:

| Area | Examples |
|------|----------|
| Policy | `GET /api/firmware/execution-policy` |
| Catalog | `GET/POST .../packages`, `GET .../packages/{id}`, artifacts POST |
| Versions | `GET/POST .../versions` |
| Table layouts | `GET .../table-profiles` |
| Targets | `GET/POST .../targets` |
| Jobs | `GET/POST .../jobs`, `GET .../jobs/{id}`, `simulate`, `approve`, `cancel` |

Register a build with `label`, optional `buildId` and optional `tableProfileId` from the table layout catalog. A table set can target a registered version independently of how many record lengths are known for it. Register builds separately when their table layouts differ.

`GET /api/firmware/table-profiles` returns the known `verifiedLengths` for each profile and the subset `terminalAcknowledgedRecords` observed on OEM emulated firmware. The remaining lengths come from compiled terminal catalogues. `completeCatalogue` indicates that all provisionable message types were identified for the selected image profile. Choose that profile only for a matching build.

!!! danger "Live flashing"
    **`Firmware:AllowLiveFlashing`** default **false**. DLA/XMODEM **`HARDWARE VALIDATION REQUIRED`**.

**UI:** `/firmware/packages`, `/firmware/jobs`, etc.
