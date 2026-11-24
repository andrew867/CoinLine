# Table sets API

**Route prefix:** `/api/tables`

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/tables/sets` | List |
| POST | `/api/tables/sets` | Create |
| GET | `/api/tables/sets/{id}` | Detail |
| PUT | `/api/tables/sets/{id}` | Update |
| PUT | `/api/tables/sets/{id}/firmware-target` | Set or clear a draft's target version using `firmwareVersionId` |
| POST | `/api/tables/sets/{id}/clone` | Create a draft copy with the same records |
| GET | `/api/tables/sets/{id}/advertising` | Read the 20 MT29 slots, record revision, SHA-256 and supported MT26 and MT30 playback settings |
| PUT | `/api/tables/sets/{id}/advertising` | Encode and save slots on a draft. Send the retrieved revision and SHA-256 to detect concurrent edits. |
| GET | `/api/tables/sets/{id}/records/{number}/fields` | Read editable fields for a known wire profile |
| POST | `/api/tables/sets/{id}/records/{number}/fields` | Start an optional record from a supported inactive template |
| PUT | `/api/tables/sets/{id}/records/{number}/fields` | Update selected fields on a draft |
| POST | `/api/tables/sets/{id}/publish` | Publish (**confirm**) |
| GET | `/api/tables/sets/{id}/assignment-preview` | Preview eligible phones for `targetType` and `targetId` |
| POST | `/api/tables/sets/{id}/assignments` | Apply a published set to the eligible phones confirmed by the preview |
| POST | `/api/tables/sets/{id}/schedules` | Schedule pickup by the eligible phones confirmed by the preview |
| GET | `/api/tables/sets/{id}/schedules` | View schedules and their most recent delivery attempts |
| GET | `/api/tables/schedules` | View schedules across sets. Filter by `terminalId` when needed. |
| POST | `/api/tables/sets/{id}/schedules/{scheduleId}/cancel` | Cancel a schedule before delivery starts or between attempts |

The assignment API supports `terminal`, `group`, `site` and `customer`. Apply the same `targetType` and `targetId` with the preview's eligible phone IDs in `terminalIds`. A changed eligible list requires another preview.

A new set accepts an optional `targetFirmwareVersionId`. A draft copy inherits the target. Published targeted sets can be assigned and downloaded by phones with the same registered firmware version. Detail responses expose `firmwareFindings`. Blocking findings prevent publishing. Other findings identify records whose format still needs verification.

For the verified 200-entry Multi-pay profile, the set review checks MT92 screening rules that use a direct tariff or mark a call as free. If screening or configured MT101 exchange rules refer to tariffs without an MT73 record in the set, the review returns a warning so a partial update can rely on the phone's installed tariff record.

Each advertising slot has `text`, `durationTicks`, `mode`, `blankBefore` and `endSequence`. Set `endSequence` to write a leading zero that ends the current rotation. To show a timed blank, send empty `text` with `endSequence` false and a positive duration. The response includes `onHookEnabled`, `quickAccessEnabled`, `callEstablishedEnabled` and `scrollTicks`. The settings are null when the respective record has no recognized layout. Durations and scroll values use 10 ms ticks.

The schedule request takes the same selection plus `availableAtUtc`, either an ISO 8601 date and time with its offset or null for immediate availability. Each phone can have one open scheduled download. A call-in can pick up the set once it is available. Failed attempts remain ready for the next call. The existing assignment stays effective until the terminal acknowledges the scheduled download. An operator assignment made during delivery takes precedence.

Field updates include the `revision`, `sha256`, `profile` and a `changes` object keyed by field ID. The API rejects stale revisions and leaves bytes outside the changed fields intact. For matching layouts with the same size, pass `profile` in the read query.

For the 353-byte MT62 numbering plan, the field response includes `numberingPlan.dial` and `numberingPlan.format`. A null value means the corresponding layout could not be decoded. A field update can include a `numberingPlan` with either or both complete rule arrays. Each row contains `id`, `noMatch`, `match`, `control`, `tag`, `callType` and `digits`. Dialing rows can also include `conversion` or `maximumDigits` when enabled by the control byte. Link values are row IDs, with null indicating an absent link. The first row is the entry point. The API repacks links, validates the match and fallback paths, and rejects edits that exceed capacity or overwrite unrecognized bytes. Use `changes` in the same request to edit currency or other numbering-plan settings. The MT62 `keyCardNumber` field is write-only.

MT26 and MT31 keep the older layout as the default when a set has no verified firmware target. The `profiles` array lists the applicable choices. Use the selected `profile` in the update. The MT31 `craftAccessCode` and `keyCardNumber` fields are write-only. Omit either key from `changes` to keep its stored value.

MT22, MT23, MT134 and MT135 have inactive templates. In the field response, `card-{row}.format` selects the standard and the editor exposes the corresponding data sections. A save can select a standard and set its new fields in the same request. Moving between card families clears the shared 20-byte area for the row. Supply three digits for a service code or an empty string to end the list. The API rejects gaps and active data-spill steps after an end marker. MT23 and MT135 accept carrier control fields, with terminal-rate permissions available on MT135. Enabling Feature Group B requires a positive screening entry reference. Set detail includes nonblocking `provisioning.findings` for carrier references and payment permissions in supported record pairs.

MT93 has two 225-byte profiles. A set with a verified firmware target uses that target's layout. Otherwise, pass the selected `profile` when opening or updating the record. The `validation-key-{index}` fields use `editor: "password-hex"` and always return a null value. To replace a slot, send exactly 16 hexadecimal characters from the firmware-formatted key material. Omit the field or send an empty string to preserve it. On the later profile, each `discount-{row}.{zone}Mode` accepts `0` for a signed adjustment, `1` for a percentage of the normal charge, `2` for a replacement tariff, and `3` for disabled. Changing the method resets that call zone's two stored values. The same request can set the new `{zone}Initial` and `{zone}Later` values.

The 1,001-byte MT72 profile has a blank template and named settings for the assigned portion of its payload. The `spareBytes` property contains 2,000 characters, with one hexadecimal pair per payload byte and `??` for each protected byte. The first pair is wire offset `1`. The message type at offset `0` is fixed. In a draft update, send `spareBytes` as an array of `{offset, value}` objects with integer offsets `1` through `1000` and integer byte values `0` through `255`. A write rejects duplicate offsets, protected bytes and bytes that are also part of a named field in `changes`. Use the write-only native fields to replace protected data. The `operatorPin` field also accepts `clear` to remove the stored PIN. Both kinds of edit use the same revision and digest checks.

The 100-byte MT94 profile has an inactive template. Use `startAt` for the UTC time on the terminal clock, without a timezone suffix. The API also accepts the individual `start.*` fields. `start.year` counts from 1900. Use one date input method per edit. `modemSpeed` accepts `1` for 1200 baud and `2` for 2400 baud. The `sector-{index}` values select sectors `0` through `255`. To replace `serverAuthentication`, supply exactly 12 hexadecimal characters. The field returns a null value.

Use `POST /api/tables/sets/{id}/schedules` for a published set containing only MT94. `availableAtUtc` determines when a call-in can pick up the record. When enabled, `startAt` must be later than `availableAtUtc`, or later than the current time for immediate availability. If a phone first calls after the enabled terminal start time has passed, the host cancels the pending delivery. A disabled record can be scheduled to clear the terminal’s code download schedule. A successful MT94-only delivery belongs to the firmware schedule history and is independent of the phone’s regular table assignment.

Visual prompt sections with `showDisplayPreview` set contain the upper and lower fields of a known two-line message in display order. Each `prompt-{row}.text` key still addresses one zero-based, 20-byte wire row. Supported Western accents are converted to the firmware's glyph codes. An unmapped imported row returns a null value and is preserved when omitted from `changes`.

Graphical records MT131 through MT133 expose fields based on the screen type. A field ending in `.kind` accepts `0` for undefined, `66` for bitmap, `77` for mixed, or `84` for text. The API can apply a type change and the new content in the same request. A type change reinitializes that screen's content area. Text uses newline-separated rows. A screen link packs the zero-based screen index into the low six bits and the panel into the high two bits. Value `255` means no link.

The MT68 `serviceKey` field is write-only in the native field API. Omit it from `changes` to keep the stored key. The field response provides `editor: "password"` and `value: null`. Enabling messaging requires a valid service number and key.

**UI:** `/table-sets`, `/table-sets/:id`
