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
| Configuration transition review | `GET .../transition-review?terminalId={id}&targetFirmwareVersionId={id}&candidateSetId={id}`; candidate is optional |
| Target management-access draft | `POST .../transition-access/preview`, `POST .../transition-access` |
| Targets | `GET/POST .../targets` |
| Jobs | `GET .../jobs`, `POST /api/terminals/{id}/firmware-jobs`, `GET .../jobs/{id}`, `simulate`, `approve`, `cancel` |

Register a build with `label`, optional `buildId` and optional `tableProfileId` from the table layout catalog. A table set can target a registered version independently of how many record lengths are known for it. Register builds separately when their table layouts differ.

`GET /api/firmware/table-profiles` returns the known `verifiedLengths` for each profile and the subset `terminalAcknowledgedRecords` observed on OEM emulated firmware. The remaining lengths are recorded in the known target catalogues. `completeCatalogue` indicates that all provisionable message types were identified for the selected image profile. `observedLengthsOnly` identifies an incomplete acknowledged subset without a fixed native field layout. Its `evidenceNote` distinguishes successful emulated cold INSTALL from a host-completed table batch where INSTALL failed. An observed length constrains the matching record, but does not rule out unobserved message types or establish field meanings. Choose that profile only for a matching build.

`GET /api/firmware/transition-review` returns source and target version metadata, assigned and candidate set identities, known record lengths, action categories, layout review flags and active or outstanding download counts. It does not return record payloads or modify the database. `configurationReadyForReview` refers only to the structural checks. `upgradeQualified` remains false until a separate device qualification workflow exists. Read permission is required for firmware jobs, firmware registry, phones, table sets, records and assignments.

The management-access draft preview accepts `terminalId`, `targetFirmwareVersionId`, `candidateSetId` and `name`. It checks the assigned source and separate target candidate, the registered MT21 lengths and phone identity. The result contains the source/candidate set IDs, source/target byte counts, changed target byte count, retained record count and `previewToken`, not access digits or raw records. Submit the same selection and name with the token to `POST .../transition-access` to create an unpublished, unassigned target draft. The request fails if the reviewed configuration changed. Only MT21 identity and manager number fields are carried forward; other candidate bytes remain unchanged. Preview requires the same read permissions as the transition review; creating the draft additionally requires Table sets Add permission. Neither endpoint changes an installation, assignment, download schedule or firmware job.

!!! danger "Live flashing"
    **`Firmware:AllowLiveFlashing`** default **false**. DLA/XMODEM **`HARDWARE VALIDATION REQUIRED`**.

To revise a registered build, preview with `{"tableProfileId":"mtr19-multipay-200"}`. Use explicit `null` to remove its verified profile reference. The preview returns affected counts, grouped findings, `canApply` and a `previewToken`. Apply with the same `tableProfileId` and token. Both endpoints require Firmware registry Update permission. An incompatible published targeted or currently assigned record, incompatible queued selection, active NCC session or active transfer blocks the change; draft findings and unobserved lengths are advisory. Scheduled and queued partial transfers are reviewed using their selected records and dependencies, not unrelated source records. A changed configuration or blocking finding returns HTTP 409. Apply updates only the registered firmware reference, its revision and audit history. It does not rewrite tables, reassign phones, schedule downloads or certify device operation.

**UI:** `/firmware/packages`, `/firmware/jobs`, etc.
