# Firmware API

**Route prefix:** `/api/firmware`

The execution-policy response distinguishes `allowLiveFlashing` and `liveDlaEnabled` from `transportMode` and `liveTransportConfigured`. Transport configuration reports whether settings for the selected live mode are present, not whether a remote endpoint is reachable or a terminal is compatible. No endpoint addresses or credentials are returned. Both execution switches remain disabled by default.

Packages, artifacts, compatibility rules, targets, jobs:

| Area | Examples |
|------|----------|
| Policy | `GET /api/firmware/execution-policy` |
| Catalog | `GET/POST .../packages`, `GET .../packages/{id}`, artifacts POST |
| Versions | `GET/POST .../versions` |
| Revise a build's layout | `POST .../versions/{id}/table-profile/preview`, `PUT .../versions/{id}/table-profile` |
| Table layouts | `GET .../table-profiles` |
| Target tariff draft | `POST .../transition-tariffs/preview`, `POST .../transition-tariffs` |
| Configuration transition review | `GET .../transition-review?terminalId={id}&targetFirmwareVersionId={id}&candidateSetId={id}`; candidate is optional |
| Target management-access draft | `POST .../transition-access/preview`, `POST .../transition-access` |
| Target 200-to-180 screening draft | `POST .../transition-screening/preview`, `POST .../transition-screening` |
| Targets | `GET/POST .../targets` |
| Prepare local dialing draft | `POST .../transition-local-dialing/preview`, `POST .../transition-local-dialing` |
| Jobs | `GET .../jobs`, `POST /api/terminals/{id}/firmware-jobs`, `GET .../jobs/{id}`, `simulate`, `approve`, `cancel` |

Register a build with `label`, optional `buildId` and optional `tableProfileId` from the table layout catalog. A table set can target a registered version independently of how many record lengths are known for it. Register builds separately when their table layouts differ.

`GET /api/firmware/table-profiles` returns the known `verifiedLengths` for each profile and the subset `terminalAcknowledgedRecords` observed on OEM emulated firmware. The remaining lengths are recorded in the known target catalogues. `completeCatalogue` indicates that all provisionable message types were identified for the selected image profile. `observedLengthsOnly` identifies an incomplete acknowledged subset without a fixed native field layout. Its `evidenceNote` distinguishes successful emulated cold INSTALL from a host-completed table batch where INSTALL failed. An observed length constrains the matching record, but does not rule out unobserved message types or establish field meanings. Choose that profile only for a matching build.

`GET /api/firmware/transition-review` returns source and target version metadata, assigned and candidate set identities, known record lengths, action categories, layout review flags and active or outstanding download counts. It does not return record payloads or modify the database. `configurationReadyForReview` refers only to the structural checks. `upgradeQualified` remains false until a separate device qualification workflow exists. Read permission is required for firmware jobs, firmware registry, phones, table sets, records and assignments.

The tariff draft preview accepts `terminalId`, `targetFirmwareVersionId`, `candidateSetId`, `name` and optional `choices` mapping each distinct source tuple's first index to a target index. It groups all 128 source rows by exact nine-byte tuple and returns classification, signed periods, charges, source indexes, allowed target positions, proposed target index and reuse/allocation action. The `candidateReview` identifies protected indexes and their included record types, reviewed, missing and unreviewed records. `changedPositions`, blocking/advisory findings, `canCreate` and `previewToken` describe the proposed draft. No route digits or raw record bytes are returned. Submit the same request and token at `POST .../transition-tariffs`; changed inputs return HTTP 409 and blocked plans return HTTP 400. The new unassigned draft modifies only allocated MT73 rows, preserving the candidate header, active rates and all other record bytes. Read permissions match transition review; creation requires Table sets Add. There is no job, publication, assignment or delivery side effect.

The management-access draft preview accepts `terminalId`, `targetFirmwareVersionId`, `candidateSetId` and `name`. It checks the assigned source and separate target candidate, the registered MT21 lengths and phone identity. The result contains the source/candidate set IDs, source/target byte counts, changed target byte count, retained record count and `previewToken`, not access digits or raw records. Submit the same selection and name with the token to `POST .../transition-access` to create an unpublished, unassigned target draft. The request fails if the reviewed configuration changed. Only MT21 identity and manager number fields are carried forward; other candidate bytes remain unchanged. Preview requires the same read permissions as the transition review; creating the draft additionally requires Table sets Add permission. Neither endpoint changes an installation, assignment, download schedule or firmware job.

The screening preview accepts the same selection and name plus optional `tariffChoices` and `carrierChoices` objects mapping source reference indexes to target reference indexes (for example `{"tariffChoices":{"0":7}}`). It returns the 200 and 180 slot counts, configured and changed slot counts, changed and cleared candidate positions, reserved/later-only counts, description and retained-record counts, exact matching reference options, blocking/advisory findings, `canCreate` and a `previewToken`. It does not return dialed digits, description text or raw records. Apply the same request and preview token at `POST .../transition-screening`; stale state or a changed mapping returns HTTP 409, unresolved findings return HTTP 400. The cloned target draft changes only common MT92 fields and source descriptions, retaining candidate reserved bytes and all other records. Access permission and draft creation gates match the management-access endpoints. There is no assignment, job or delivery side effect.

The local dialing draft preview accepts `terminalId`, `targetFirmwareVersionId`, `candidateSetId`, `name`, and optional `rateChoices` (source MT73 index to target local index) and `areaChoices` (source segment number to target segment number). The result lists exact tuple matches, all four candidate local rates, active source areas, eligible target records, changed exchange-code and rate-tuple counts, and blocking/advisory findings. Submit the same selection with `previewToken` to `POST .../transition-local-dialing` to create a new unassigned draft. A new source/candidate revision or mapping requires a fresh preview. Unrelated target segments and MT73 remain unchanged. The preview needs the transition-review read permissions; creation also needs Table sets Add. Neither endpoint schedules work or changes an installed phone.

!!! danger "Live flashing"
    **`Firmware:AllowLiveFlashing`** default **false**. DLA/XMODEM **`HARDWARE VALIDATION REQUIRED`**.

The job detail exposes `liveExecutionConfigured`, which reports whether both execution policies and the selected endpoint settings are present. It does not establish connectivity, authorization of a particular job or device compatibility. `POST .../execute-live` validates the configured transport and registered artifact before atomically claiming an approved job. An unavailable transport configuration returns HTTP 409 without changing approval, status or attempt history. A configured endpoint that fails after the claim remains a recorded attempt.

To revise a registered build, preview with `{"tableProfileId":"mtr19-multipay-200"}`. Use explicit `null` to remove its verified profile reference. The preview returns affected counts, grouped findings, `canApply` and a `previewToken`. Apply with the same `tableProfileId` and token. Both endpoints require Firmware registry Update permission. An incompatible published targeted or currently assigned record, incompatible queued selection, active NCC session or active transfer blocks the change; draft findings and unobserved lengths are advisory. Scheduled and queued partial transfers are reviewed using their selected records and dependencies, not unrelated source records. A changed configuration or blocking finding returns HTTP 409. Apply updates only the registered firmware reference, its revision and audit history. It does not rewrite tables, reassign phones, schedule downloads or certify device operation.

**UI:** `/firmware/packages`, `/firmware/jobs`, etc.
