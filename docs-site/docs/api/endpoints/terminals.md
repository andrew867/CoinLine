# Terminals API

**Route prefix:** `/api/terminals`

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/terminals` | List |
| POST | `/api/terminals` | Create |
| GET | `/api/terminals/create-options` | Minimal site, firmware, family and permitted group choices for phone registration |
| GET | `/api/terminals/{id}` | Detail |
| GET | `/api/terminals/{id}/routine-call-in` | Assigned and acknowledged MT35 settings and latest observed routine contact; source evidence is permission-scoped |
| GET | `/api/terminals/firmware-options` | Minimal installed-version choices for phone inventory |
| GET | `/api/terminals/{id}/table-assignment-options` | Minimal table-set choices for phone assignments, excluding firmware-scheduler-only sets |
| GET | `/api/terminals/{id}/lifecycle` | Manager activity, lifecycle dates and current revision |
| PUT | `/api/terminals/{id}/lifecycle` | Deactivate, amend deletion review date, or reactivate |
| PUT | `/api/terminals/{id}` | Update |
| GET | `/api/terminals/{id}/identity` | Read the current and previous NCC IDs, previous terminal type and the first matched contact since the ID change |
| PUT | `/api/terminals/{id}/identity` | Change only the Manager's NCC ID with the current ID as a precondition |
| GET | `/api/terminals/{id}/inventory` | Read site details, administrative fields, equipment and reported hardware serial |
| GET | `/api/terminals/{id}/cashbox` | Read the latest reported status by cashbox and the phone’s collection history |
| PUT | `/api/terminals/{id}/inventory` | Replace the terminal inventory profile using its current revision |
| GET | `/api/terminals/families` | List terminal family IDs, release names, variants and Maximizer type numbers |
| GET | `/api/terminals/regions` | List region codes currently in use and their phone counts |
| GET | `/api/terminals/time-areas` | List time-area codes currently in use and their phone counts |
| PUT | `/api/terminals/{id}/family` | Set `terminalFamilyId` or clear it with null |
| PUT | `/api/terminals/{id}/firmware` | Register `firmwareVersionId` for the installed build or use null for unknown |
| GET | `/api/terminals/{id}/groups` | Read current memberships and groups available to this phone |
| PUT | `/api/terminals/{id}/groups` | Save up to 40 group IDs with the retrieved revision. The first ID is the primary group. |
| GET | `/api/terminals/{id}/events` | Events |
| POST | `/api/terminals/{id}/status` | Status |
| GET | `/api/terminals/{id}/table-assignment` | Assignment |
| POST | `/api/terminals/{id}/table-assignment` | Assign (confirm) |
| POST | `/api/terminals/{id}/table-assignment/rollback` | Rollback (confirm) |
| GET | `/api/terminals/{id}/special-callback` | Read the latest callback and its acknowledgment and return timestamps |
| PUT | `/api/terminals/{id}/special-callback` | Queue or replace a callback |
| POST | `/api/terminals/{id}/special-callback/cancel` | Cancel future offers for the current revision |
| POST | `/api/terminals/{id}/downloads` | Start download (idempotency) |
| POST | `/api/terminals/{id}/firmware-jobs` | Create firmware job |
| GET | `/api/terminals/{id}/diagnostics` | Diagnostics |
| POST | `/api/terminals/{id}/diagnostics/snapshots` | Snapshot |
| POST | `/api/terminals/{id}/request-cdr-upload` | Request CDR upload |
| POST | `/api/terminals/{id}/request-table-reload` | Request table reload |

Create accepts an optional `terminalFamilyId`. The terminal list accepts the same field as a query filter and includes the family label. It also returns `regionCode` and accepts it as a case-insensitive exact filter. A phone can have a terminal family and a separately registered firmware build. Wire settings such as card-only or Multi-pay mode are configured in MT26.

To create a phone or change its Manager NCC ID, send `terminalIdHex` with ten hexadecimal digits. To change only the ID, also send `expectedTerminalIdHex` with the currently registered value. IDs compare case-insensitively. Another phone's ID or a stale expected ID returns HTTP 409. The GET response timestamps the previous ID and type from their saved catalog changes. `firstContactAtUtc` is null until the host records a matched NCC frame under the new ID. A repeated change back to an older ID starts a new observation period. Phones registered before this history feature begin recording changes at their next edit.

Send the complete inventory `profile` from GET with the current `revision` to replace it. The initial revision is null and competing updates return HTTP 409. Send administrative codes as strings to retain leading zeroes. Use decimal costs with at most two fractional digits and integer equipment counts. The hardware serial comes from the terminal's NCC status, and the registered location and customer come from the existing terminal assignment.

The optional `timeAreaCode` is two letters or digits, or a sign and one digit. It selects phones for MT35 or MT94 schedules. A phone with a blank code is outside every time-area scope.

On the cashbox endpoint, `statuses` contains one latest report per box. `isCurrent` identifies the report eligible for phone restoration. `collections` distinguishes `totalMinorUnits` from `removedBoxBalanceMinorUnits`, which describes the first removed box if the phone combined collections. Coin counts follow the terminal's MT50 slot order. Call quotes and CDR payment amounts remain in their own records.

**Phone groups** use `GET /api/terminal-groups` to list groups and `POST /api/terminal-groups` to create one. Set optional `code` to three to six letters or digits and use `description` for the operator description. To update these fields, send `name`, `code`, `description` and the current `revision` to `PUT /api/terminal-groups/{id}/details`. The existing name-only edit is available at `PUT /api/terminal-groups/{id}`.

`GET /api/terminal-groups/{id}/phones` returns current members and eligible phones with their membership, limit and revision. Pass `membersOnly=true` to retrieve just the members. Send an array of `terminalId`, `revision` and explicit `member` values to `PUT /api/terminal-groups/{id}/phones` to apply only the listed changes. An invalid or stale change rejects the whole request.

For a special callback, supply the current `revision` or null when creating the first request. Supply `dueAtUtc` as an ISO 8601 timestamp with an offset, or null to request the earliest callback. Use `terminalClockOffsetMinutes` for a phone clock offset from UTC. Set `deliverAssignedTables` to opt into sending the current compatible assignment on return. The GET response wraps the record in `callback`, which is null before the first request. Cancellation takes the current `revision`. A stale revision returns HTTP 409.

**Confirm flags:** several POST routes require `?confirm=true` — see Swagger.

**Manager lifecycle:** use `active=true` or `active=false` on the terminal list to filter activity independently of reported operational status. To deactivate, PUT `active: false` and the observed `revision`; optionally supply a `deletionReviewDate` (YYYY-MM-DD), otherwise the initial date is 45 days from current UTC date. To reactivate, PUT `active: true` and the observed revision; this clears both dates. An open local NCC link or stale revision returns HTTP 409. An inactive terminal is excluded from new table deliveries and field request creation. The review date does not trigger record deletion.

The firmware and table assignment choices are separately permissioned under Phones Read and Table assignments Read. They do not grant access to firmware registry or table authoring endpoints. The current assignment response includes the set name and target firmware label and ID, so a read-only assignment operator need not retrieve the whole table catalogue.

The assignment choices also include `compatibilityIssue` (null if there is no known conflict) for the installed profile and all records in each set. Direct assignment and rollback check again before changing the assignment. A known length, complete catalogue or explicitly confirmed layout conflict is returned as HTTP 400 with its message type. The target-firmware ID constraint still applies.

Phone registration choices require Phones Add and do not require full site, group, firmware or phone-list access. A group choice is returned only if the operator also has Membership Add. A new phone must use an existing site; its optional group must belong to the site's customer or be a global group.

**UI:** `/terminals`, `/terminals/:id`

**Entity:** `Terminal`, related downloads/jobs
