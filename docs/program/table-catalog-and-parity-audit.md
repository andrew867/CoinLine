# CoinLine terminal table catalogue and parity audit

The legacy Telco Maximizer desktop client exposed configuration editors, group assignment, and download scheduling. Its archived WinHelp identifies the operator fields and workflows. Static inspection of the desktop executable and its decompilation confirms that it had an advertising simulator, loaded its preview timing from workstation settings, and tracked advertising as a download category. The desktop client used RSC to work with Manager data. The terminal message catalogues and packed layouts establish the separate NCC/DLOG wire formats. The physical Manager server database schema and every RSC-to-DLOG conversion remain unverified.

This audit covers the configuration topics exposed in the archived desktop help and the provisionable record families identified in the available terminal revisions. An MT is the decimal DLOG message type. A byte count includes the leading MT byte where stated. Firmware profile means the actual target build and feature set, including terminal model, languages and dial-table compression.

## Current state

- **Working distribution.** CoinLine can version, hash, publish and assign externally produced binary records. A matched MT44 starts a download. The host sends MT18, paces fragments with NCC link ACKs, tracks MT14 per record and closes the table exchange with MT13. Warm OEM and cold demo firmware accepted the same ten-record lab set. The web console shows definitions, versions, sets, assignments and download results.
- **Partially implemented content.** All ten lab records were imported as bytes. The active tests exercised the magnetic-card bench check, three coin denominations and a free call-screening route. Other records were often minimal, defaults or inactive. The live rate quote verified the host's rating engine and MT64 codec. Terminal-side offline tariff coverage remains open.
- **Authoring.** The table set editor now offers typed native fields for identified older and later wire profiles. It also supports the 20-slot text advertising editor and inactive templates for selected optional records. Edits preserve all bytes outside the selected fields. Unknown profiles retain the binary import workflow. Registry coverage, authoring and firmware behavior are separate milestones.

The initial lab database exposed definitions for MT21, 22, 26, 31, 33, 50, 62, 73, 92, 93 and 101, plus three old seed entries. The repository seeds short test vectors for MT21, 26, 31, 33 and 73. Those two-byte fixtures are useful for storage tests. A real MT44 transfer rejects them. The ten-record lab set was imported separately. A subsequent clone kept those ten records byte for byte and added MT29. The host recorded MT14 acknowledgments for all eleven and marked the batch completed.

## Master list of terminal records

**Lab** means a record with the listed size received an application-level MT14 on the tested firmware. **Exercised** identifies a feature observed after transfer. **Fixture** means CoinLine has a short catalogued test vector. **Open** means CoinLine has no verified transfer for that record or profile. Native authoring coverage is listed below.

| MT | Table or profile | Fields and legacy Manager surface | CoinLine status and remaining validation |
| --- | --- | --- | --- |
| 21 | NCC access and terminal parameters | On-wire terminal identity, primary and alternate access digits, call routing codes. Terminal Information / Call Setup. | **Lab, 18 B.** Follow-up management contacts worked. Validate how authored identity and alternate access changes are staged and read back. |
| 22 / 134 | Magnetic and calling-card acceptance | Card ranges, format, expiry/service-code positions, validation mode and carrier. Credit/Calling/Smart Cards. The two IDs are revision-specific. | **MT22 Lab, 661 B, exercised.** Populated profile passed a synthetic stripe read and bench acceptance. MT134, wider card formats and online approval remain open. |
| 23 / 135 | Carrier table and screens | Carrier references, prompts, access or carrier-selection fields, routing and rating references. Carrier Screens. | **Open on device.** Both record types are registered and have native editors. Verify screening, rates and prompt references on the target firmware. |
| 26 | Feature and call options | Language, advertising enablement, rating mode, card-validation policy, call options, data jack and alarm/CDR settings. Universal Terminal Features / Interactive Interface. | **Lab, 72 B.** Full flag interactions and authoring remain open. |
| 27 / 28 and 85 / 86 | Visual prompts, languages 1 and 2 | Indexed terminal prompts for call states and messages, selected by terminal language. MT27/28 appear in the older catalogue. MT85/86 appear in the later catalogue. | **Open on device.** The editor exposes indexed text. Verify the named prompt map and rendering on each target. |
| 29 | Text advertising | Rotating display text, per-slot duration, display mode and attributes. Advertising / Edit Entry / Edit Block / Simulate. | **Lab, 481 B, exercised.** CoinLine has a native slot and block editor, sequence simulator and codec. The OEM firmware acknowledged the record and rendered both test messages on hook. |
| 30 | User interface parameters | Key, dialing, hardware and authorization timers, scroll timing, prompt and interaction controls. Call Setup / Interactive Interface. | **Open.** Existing definition under MT30 is a mislabeled seed. Validate timer units, ranges and live behavior. |
| 31 | Installation and service parameters | Predial strings, craft access, analog prefix, packet delays/retries and service behavior. Terminal Information / Call Setup. | **Lab, 37 B.** Default-based payload loaded. Validate changes to predial and service parameters. |
| 32 | Communications statistics parameters | Upload thresholds and windows, timing intervals and timestamp settings. CDR & Carrier Statistics. | **Open.** Exercise scheduled communications reports. MT37 is the related upload. |
| 33 | Modem parameters | Modem setup and link parameters. Call Setup / Interactive Interface. | **Fixture.** The two-byte seed cannot support a real transfer. Verify modem profile and effective settings. |
| 34 | Call and carrier statistics parameters | Summary and detail thresholds, upload windows, timestamps and enablement. CDR & Carrier Statistics. | **Open.** Verify scheduling and correlate resulting uploads. |
| 35 | Time and call-in parameters | Routine call-in schedule, retry settings and timing windows. Terminal Information / Interactive Interface. | **Open.** Time sync already works as a separate dialogue. Verify a table-triggered scheduled call. |
| 49 | Answer supervision parameters | Supervision patterns and timing settings. Call Setup. | **Open.** The lab passed its normal answer-supervision check using existing defaults. No MT49 download was verified. Confirm target support. |
| 50 | Coin validation | Coin windows, values and volumes, enable flags, cash-box and escrow thresholds. Coin/service configuration. | **Lab, 105 B, exercised.** OEM install accepted three synthetic coin denominations. Full calibration and rejection boundary tests remain open. |
| 55 | Quick Access Keys / repertory dialer | Destination, upper/lower display text, key flags, call type and optional rate index. Quick Access Keys. | **Open.** Validate key behavior, rate references and model-specific slot counts. |
| 58 | Service Level Restrictions | Restriction level and activation window. Service Level Restrictions. | **Open.** Verify restricted service, expiry and override behavior. |
| 59 / 92 | Call-Screening List | Dial patterns and wildcards, allow/deny/free flags, call type, answer supervision and rate index. MT59 is an older, smaller list. | **MT92 Lab, 3,401 B, exercised.** The tested 200-slot profile routed the free call without MT63 when the free flag and rate index `0xFF` were both set. Test priority, block and wildcard cases. MT59 is registered and remains unverified on device. |
| 62 | Numbering plan | Dial parsing and translation, digit format, international handling, digit timers and local dialing search mode. | **Lab, 353 B.** Default-based payload loaded. Broader rule and digit-translation coverage remains open. |
| 68 | Optional messaging service | Audio-Gram Delivery System settings. The help and firmware layout agree on switch access, call and payment permissions, surcharges, reporting and signaling fields. Service statistics upload separately as MT69. | **Native editor, 72 B.** Inactive template and editable access, reporting, permissions, surcharges, tones, firmware timers, and a write-only service key. Verify the legacy voice-prompt delay mapping and the switch dialogue on compatible firmware. |
| 72 | Spare table / electronic purse parameters | Feature-dependent application partitions and parameters. Spare Table. | **Open.** Establish each enabled profile and its field and secret-handling requirements. |
| 73 | Terminal tariff and rate ladders | Initial, subsequent and additional charge/duration steps and indexed tariff records. Rate Table. | **Lab, 1,192 B.** A lab record loaded. Verify terminal-computed multi-step and offline rates. Host quote editing and MT63/64 are already available separately. |
| 74-81, 90-91 | Uncompressed NPA/NXX local dialing segments 1-10 | Area-code/exchange search and rating classification data. Availability varies by firmware. | **Open.** Build and exercise the uncompressed segmentation profile. |
| 93 | Smart-card parameters | Application settings, embedded discount and value rules, and protected key material. Credit/Calling/Smart Cards. | **Lab, 225 B.** An inactive lab profile loaded. Smart-card processing and protected production provisioning remain open. |
| 94 | Remote code download parameters | Code-server route, schedule, baud, target revision, retries and section selection. Download Code Scheduler. | **Open on device.** CoinLine has firmware package/job management. The MT94 provisioning and actual firmware update path remain unverified. |
| 95 | Instant Win | Eligible smart-card serial ranges, application flags and display prompts. Instant Win. | **Open.** Feature-gated and not represented by the independent seed under MT30. |
| 98 / 99 | Visual prompts, languages 3 and 4 | Additional indexed prompts on builds that expose the language slots. | **Open.** Establish target support and test language switching and rendering. |
| 101 | Compressed NPA/NXX local dialing segment 1 | First page of compressed local exchange/rating data. | **Lab, 403 B.** One segment loaded. A rated call reached the host. Composite search, routing and offline behavior remain open. |
| 102-115 | Compressed NPA/NXX segments 2-15 | Additional local dialing and classification pages. Support depends on firmware build. | **Open.** Verify segment count, ordering, boundary lookups and inter-page links. |
| 131-133 | Graphical terminal tables 1-3 | Graphic screen IDs, assets, text/mixed screens, rotation and linkage to call functions. Advertising Graphic. | **Partial.** Native metadata and bitmap editing for graphics records. Mixed and text layouts, device compatibility and hardware results remain open. MT153 is a usage report. |
| 136-149, 154-155 | Double-compressed NPA/NXX segments 1-16 | Alternate compression profile with a separate type range and ordering. | **Open.** Verify codec and round-trip across all supported segment boundaries. |
| 150 | NPA set-based rating | Area-code-to-tariff-index lookup. NPA Rating. | **Open.** Test lookups and valid references to MT73. |
| 151 | International set-based rating | Country/destination-to-tariff-index lookup. International Rating. | **Open.** Test normalization, fallback and tariff references. |
| 152 | Holidays and time-of-day discounts | Calendar dates, time windows, discount bands and tariff pointers. Discount Rate Table and Discount Time of Day are separate screens for this record. | **Open.** Test calendar boundaries, overnight windows and the linked tariff steps. |

MT18 is the table-transfer header, MT44 is the terminal's request, MT14 is its application result and MT13 ends the exchange. MT10 reports terminal status and MT20 carries time synchronization. Reporting messages such as MT37, 56, 57/71, 69 and 153 upload observations. The transfer header, requests, results and reports do not belong in table sets. In particular, the seeded MT10 “Rate table”, MT20 “LCD / NPA-NXX”, and MT30 “Instant-win” labels conflict with actual wire assignments. The correct tariff and instant-win types are MT73 and MT95 respectively, and the local-dialing numbers depend on compression profile.

## Advertising text field map

The legacy desktop supported named 20-slot advertising configurations, individual and block editing, and simulation. Its help describes showing the full sequence while on hook. With call-established advertising enabled, the sequence splits into the first ten for on-hook display and the last ten for established calls. Feature switches live with the terminal's universal feature configuration. Scroll timing is a user-interface parameter. Preview flash timing in the Windows executable comes from workstation settings.

| Editor or data field | Placement on the inspected 20-slot profile |
| --- | --- |
| Record type | Byte 0 is MT29. The record is `1 + (20 × 24) = 481` bytes. |
| Duration | First two bytes of each slot, unsigned little-endian, in 10 ms units. |
| Display mode and options | Third byte of each slot. The low nibble selects the display mode. Separate attribute bits control blank-before and line position. Static, flashing and scroll-in/out modes are present. |
| Reserved profile byte | Fourth byte of each slot. Preserve it on import/export. |
| Message | Final 20 bytes of each slot. A leading zero ends the current rotation. Space padding fills active messages, including deliberate timed blanks. The native editor validates printable ASCII for new text. |
| Profile name, enabled state, revision, assignment | Name, revision and assignment live in the Manager or CoinLine. Feature enablement and scrolling settings use other terminal configuration records. |

The codec preserves imported message bytes on an unchanged edit, including padding and sequence markers. CoinLine previews all six display modes on the lower line, using MT26 to select the slots for on-hook, quick-access or established-call playback. Preview timing can use the MTR 1.9 constants and the MT30 scroll setting, or adjustable desktop-style intervals. On a phone, call state supplies the upper row. The lab set has two three-second static prompts and confirmed MT14. The emulator VFD bus trace shows both complete prompts repeatedly on hook. Flash and scroll timing, quick-access and established-call playback still need device acceptance. Visual language prompts and graphical screens have separate records and acceptance tests.

## Manager data and workflows

| Legacy surface | Current CoinLine coverage | Work remaining |
| --- | --- | --- |
| Terminal Information, sites, customer ownership and Groups | Terminal, site and customer records. A phone can participate in up to 40 groups, and each membership contributes to group assignment previews. NCC identity is matched against the terminal's on-wire ID. | Complete the per-field legacy mapping, especially the split between inventory, firmware profile, and terminal parameters. |
| Configuration naming, version history and update by group | Generic definitions, draft copies, versioned sets, publish, and assignment or scheduled pickup for phone, group, location or customer. The console tracks schedule availability and individual delivery attempts. | Verify routine pickup on OEM firmware, immediate callback and per-device firmware eligibility. Group, location and customer schedules capture the eligible phones at submission. |
| Call Setup and Interactive Interface | Stored firmware records for several relevant types, plus basic fleet and craft surfaces. | Map each screen across the underlying types and verify live settings and behavior. |
| Audio-Gram Delivery System | MT68 has a native editor for the identified fields. MT69 reports service statistics. Both correspond to the workflow described in the desktop help. | Resolve the legacy voice-prompt delay setting and verify delivery and reporting with a compatible switch service. |
| Inmate Feature Configuration | The desktop help identifies a separate download for PIN entry strategy, call handling, voice prompts, disconnect warnings, recorder signaling and timers. | Identify the record type and matching terminal profile. The inspected Multi-pay catalogues do not identify the dedicated download. |
| CDRs, performance and carrier statistics, and field alarms | Session and field-event views, call records and upload tools exist. Alarm and maintenance replies have been demonstrated. | Verify legacy live CDR ingestion and acknowledgment, carrier statistics, reporting schedules and reconciliation. |
| Cards, payments and reconciliation | Card product/account, payment and reconciliation models and screens. The deployed business path is simulated. | Connect a real authorization and settlement service before enabling approvals. Validate the independent terminal CDR flow. |
| Users and administrative preferences | Operator accounts, roles and settings. | Compare full desktop authorization, workstation defaults and configuration privileges with the current web workflow. |

## Native editor coverage

The WebUI recognizes editable record profiles by wire type and length. It selects the matching layout for records verified on a registered target build. When the same size describes multiple layouts and the set has no verified selection, the editor asks which layout to use. An edit applies named fields to the existing record and retains surrounding bytes, bit flags, credentials and reserved regions. Quick-access, service restriction, promotion and discount profiles also have inactive draft templates. The table-set workflow supports review, publish and assignment without requiring a new deployment.

Phone inventory identifies the registered firmware build. A table set can target one registered version. Assignment previews, direct assignments, and NCC pickup check that version. The selectable MTR 1.9 Multi-pay profile covers eleven lab-acknowledged record lengths. The remaining builds and feature combinations need their own format verification. Firmware declarations and records with unverified lengths still need device evidence.

### Maximizer terminal type references

These are the desktop terminal type numbers described in the terminal-specific help topics. CoinLine's operating mode field is part of MT26. Terminal type numbers identify a reference family for comparing its required and optional table lists with the actual build.

| Firmware topic | Desktop terminal types |
| --- | --- |
| MSR 1.6 | Card 8, Multi-pay 9, Desk 11 |
| MSR 1.7 Card | Basic 12, Generic 23 |
| MSR 1.7 Multi-pay | Basic 13, data jack 14, Generic 2 24, Generic 2 with data jack 26 |
| MSR 1.7 Desk, Inmate, and Coin Basic | Basic Desk 15, Generic 2 Desk 25, Inmate 16, Coin Basic 19 |
| MSR 1.8 Multi-pay | Audio-Gram 18 |
| MTR 1.9 | Card 27, Multi-pay 28, Desk 29, Coin Basic 31 |
| R1 International | Multi-pay 32, Desk 33 |
| MTR 1.11 | Card 43, Multi-pay 44, Desk 45, Coin Basic listed as 45 or 49 |
| MTR 2.0 | Card 35, Multi-pay 36, Desk 37, Coin Basic 38, Card graphical 39, Multi-pay graphical 40 |
| E-purse | MTR 2.1 Visa 21, MTR 2.2 Mondex 22, MTR 2.3 SmartCity 34, MTR 2.4 Proton 41 |

The MTR 1.11 help topics list type 45 for both Desk and Coin Basic. Retain the terminal family and firmware release when comparing those records. The Multi-pay help also lists expanded visual prompts, while the lab validation tested the records recorded in the lab catalogue above. Establish required records per build before enforcing their presence.

| Workflow | Available native fields | Work still required for full parity |
| --- | --- | --- |
| Terminal and call setup | Management access, terminal operating type, card authorization options, coin call options, advertising and display switches, user-interface timers, modem and installation timing, answer supervision and call-in settings. | Complete firmware-specific option sets, separate terminal hardware inventory from operating mode, and exercise edited data on each compatible target. |
| Card, coin and carrier | Card ranges and formats, coin values and validator enablement, default carrier and card acceptance per carrier, and smart-card rebates or tariff indices for the selected profile. | Complete product-specific validation rules, smart-card account and key management and provider-backed authorization. |
| Dialing and rating | Quick-access keys, service restriction windows, call-screening patterns and payment types, existing numbering-plan rule digits and call behavior, rate ladders, local dialing segments, NPA and international ratings, holidays and discounts. | Rule graph construction, number formatting, cross-record reference checks and firmware behavior tests for older and later profiles. |
| Advertising and display | Text advertising with a display preview, indexed language prompts, monochrome artwork preview and import, graphical screen rotation, button destinations and promotion prompts. | Mixed and text screen authoring, named language prompt catalogue, and device validation for graphical and language profiles. |
| Operations and optional services | Statistics reporting intervals, code download schedule, optional messaging, stored-value collection and alternate operator access settings. | Remaining e-purse fields and profile validation, server-side Audio-Gram and inmate workflows, and controlled validation of code download settings. |

The source check included the archived WinHelp project, RTF topics and screen references for call screening, quick-access keys, service levels, advertising, carrier screens and rating. CoinLine uses those workflows in its own interface. The archive confirms the desktop screen responsibilities and does not establish that each server setting has a terminal wire field.

## Runtime card and rating calls

An ordinary assigned install set transfers on MT44. A due scheduled download can also transfer on a routine MT8 call-in or MT9 callback. A runtime authorization or quote exchange returns its application reply after the NCC link ACK. The terminal can then acknowledge and clear the call.

| Runtime dialogue | Current coverage | Remaining work |
| --- | --- | --- |
| MT63 real-time rate request → MT64 | The published host plan supplies the quote. Prior OEM firmware accepted, displayed and cleared an actual quote. | Broader plan and firmware profiles remain to be exercised. |
| MT1 formatted card authorization → MT3, MT2 extended authorization → MT65 | The listener recognizes both formats and returns a protocol decline to matched terminals. Raw bytes from directly received authorization and card CDR records are excluded from listener session traces. | Obtain a provider integration and exercise both a decline and a provider-backed approval using a synthetic card in the emulator. |
| MT4/53 card/call detail upload → MT5 | The listener acknowledges the link frame and does not start a table transfer. | Live application acknowledgment and secure, durable ingestion remain open. |

The install-bench MT22 card acceptance demonstrates the terminal's local card policy and stripe-reader path. The real-time quote uses a host rate plan. Both are independent of the runtime card authorization decision.

In-service payment acceptance still needs a separate OEM run. Check whether a rated call enters coin collection with the enabled MT50 slots, and check which carrier reference and MT22 range the terminal selects for the synthetic stripe. Record whether a card request reaches the host before investigating the authorization response.

## Suggested implementation order

1. Expand the verified firmware profiles beyond the lab Multi-pay build. Verify later table lengths against matching terminal images before adding them to the catalog.
2. Complete the display and behavior checks for MT29 across the supported terminal builds.
3. Complete the field coverage in the newer editors. Validate route and tariff references across MT92, MT73, MT62 and NPA/NXX records before publication.
4. Work through the remaining operational and optional features with a specific target build and a corresponding behavior test. Keep per-call rating and provider-backed card authorization in their own acceptance tracks.

## Queued payment and terminal configuration checks

| Work item | Evidence to collect and acceptance criteria |
| --- | --- |
| Terminal type and accepted payment methods | Native fields are available for the firmware operating type and payment settings in MT26 and the associated MT22/134, MT50 and MT92 records. A TCP integration test confirmed that a draft MT26 edit reached the wire byte for byte. Confirm the selected mode survives an OEM download and is used in service. |
| Multi-pay phone accepts bench coins but rejects them in service | Reproduce on the installed profile with the same denominations during the install check and a rated call. Compare coin hardware events, the payment and call classification selected by firmware, the downloaded MT26/MT50/MT92 settings and the resulting display. Confirm an allowed coin-funded call recognizes a deposit and a card-only route still enforces its intended policy. |
| Synthetic magnetic card rejected for carrier after rating | Capture the selected carrier index and rate response and check the installed card range, validation action and carrier routing. Determine whether rejection occurs before any MT1/MT2 authorization. Exercise local acceptance, a provider-backed approval or controlled decline, and correct call completion as separate checks. |

## Evidence inspected

- Legacy desktop WinHelp topics for Configuration Management, Terminal Management, Advertising, Edit Entry, Edit Block and Simulate, cross-checked against the archived executable's decompiled configuration and preview paths.
- Older and later terminal DLOG message definitions, dispatch catalogues, packed table layouts and feature guards. Observed lab sizes and applications come from the [emulator acceptance record](phase-5-gate-status.md).
- CoinLine implementation of [table distribution](../../backend/src/HostPlatform.Infrastructure/Tables/TableDistributionService.cs), [NCC dispatch](../../backend/src/HostPlatform.Api/Hosting/NccLinkProcessor.cs), the [DLOG registry](../../backend/src/HostPlatform.Protocols.Dlog/DlogMessageTypeRegistry.cs) and [TCP integration tests](../../backend/tests/HostPlatform.Tests.Integration/NccOperationalTests.cs).
