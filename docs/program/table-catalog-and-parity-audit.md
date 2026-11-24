# CoinLine terminal table catalogue and parity audit

The legacy Telco Maximizer desktop client exposed configuration editors, group assignment, and download scheduling. Its archived WinHelp identifies the operator fields and workflows. Static inspection of the desktop executable and its decompilation confirms that it had an advertising simulator, loaded its preview timing from workstation settings, and tracked advertising as a download category. The desktop client used RSC to work with Manager data. The terminal message catalogues and packed layouts establish the separate NCC/DLOG wire formats. The physical Manager server database schema and every RSC-to-DLOG conversion remain unverified.

This audit covers the configuration topics exposed in the archived desktop help and the provisionable record families identified in the available terminal revisions. An MT is the decimal DLOG message type. A byte count includes the leading MT byte where stated. Firmware profile means the actual target build and feature set, including terminal model, languages and dial-table compression.

## Current state

- **Working distribution.** CoinLine can version, hash, publish and assign externally produced binary records. A matched MT44 starts a download. The host sends MT18, paces fragments with NCC link ACKs, tracks MT14 per record and closes the table exchange with MT13. Warm OEM and cold demo firmware accepted the same ten-record lab set. The web console shows definitions, versions, sets, assignments and download results.
- **Partially implemented content.** All ten lab records were imported as bytes. The active tests exercised the magnetic-card bench check, three coin denominations and a free call-screening route. Other records were often minimal, defaults or inactive. The live rate quote verified the host's rating engine and MT64 codec. Terminal-side offline tariff coverage remains open.
- **Authoring.** The table set editor now offers typed native fields for identified older and later wire profiles. It also supports the 20-slot text advertising editor and inactive templates for selected optional records. Edits preserve all bytes outside the selected fields. Unknown profiles retain the binary import workflow. Registry coverage, authoring and firmware behavior are separate milestones.

The initial lab database exposed definitions for MT21, 22, 26, 31, 33, 50, 62, 73, 92, 93 and 101, plus three old seed entries. The repository seeds short test vectors for MT21, 26, 31, 33 and 73. Those two-byte fixtures are useful for storage tests. A real MT44 transfer rejects them. The ten-record lab set was imported separately. A subsequent clone kept those ten records byte for byte and added MT29. The host recorded MT14 acknowledgments for all eleven and marked the batch completed.

## Master list of terminal records

**Native editor** identifies the profiles CoinLine can edit. **Lab** means a record with the listed size received an application-level MT14 on the tested firmware. **Exercised** identifies a feature observed after transfer. **Fixture** identifies a short catalogued test vector. The final column lists outstanding checks for each record.

| MT | Table or profile | Fields and legacy Manager surface | CoinLine status and remaining validation |
| --- | --- | --- | --- |
| 21 | NCC access and terminal parameters | On-wire terminal identity and primary and alternate access digits. The later 40-byte profile adds ADSI identifiers. Terminal Information / Call Setup. | **Lab, 18 B.** Both sizes have native fields. Follow-up management contacts worked with the 18-byte profile. Verify authored access changes and the later ADSI dialogue. |
| 22 / 134 | Magnetic, calling and stored-value card acceptance | Card ranges, standards, stripe offsets, validation mode, service codes and carrier. Credit/Calling/Smart Cards. The later record includes data spill and reload options. | **MT22 Lab, 661 B, exercised.** A populated profile passed a synthetic stripe read and bench acceptance. Both records have native editors and inactive templates. Verify edited service codes, MT134 data spill, stored-value provisioning and online approval on compatible firmware. |
| 23 / 135 | Carrier table and screens | Carrier references, prompts, card and coin acceptance, Feature Group B, screening references and terminal rate permissions. Carrier Screens. | **Native editors and inactive templates.** Both support per-carrier payment controls, routing and timing. MT135 adds terminal rate permissions. Provisioning review checks card and coin carrier references. Verify written records, carrier selection and screening on the target firmware. |
| 26 | Feature and call options | Language, advertising enablement, rating mode, card-validation policy, call options, data port and alarm/CDR settings. Universal Terminal Features / Interactive Interface. | **Lab, 72 B.** Native editors expose older and later field maps. Some bytes mean different things at the same record length. Verify flag interactions and the later profile on device. |
| 27 / 28 and 85 / 86 | Visual prompts, languages 1 and 2 | Indexed terminal prompts for call states and messages, selected by terminal language. MT27/28 use 140 display rows. MT85/86 use 400 rows. | **Native editor.** Named message IDs, paired 20-cell rows, display preview, and supported Western accents. Verify downloaded text and language switching on each target. |
| 29 | Text advertising | Rotating display text, per-slot duration, display mode and attributes. Advertising / Edit Entry / Edit Block / Simulate. | **Lab, 481 B, exercised.** CoinLine has a native slot and block editor, sequence simulator and codec. The OEM firmware acknowledged the record and rendered both test messages on hook. |
| 30 | User interface parameters | Key, dialing, hardware and authorization timers, scroll timing, prompt and interaction controls. Call Setup / Interactive Interface. | **Native editor, 68 B.** The full-width profile has prompt, call, carrier access and data-port timers. The short MT30 seed is mislabeled. Verify edited timer behavior on device. |
| 31 | Installation and service parameters | Predial strings, craft access, analog prefix, packet delays/retries and service behavior. Terminal Information / Call Setup. | **Lab, 37 B.** Older profile loaded with lab defaults. Both profiles offer write-only craft fields. The later profile adds analog prefix, manager predial and other service controls. Verify edited settings on device. |
| 32 | Communications statistics parameters | Upload thresholds and windows, timing intervals and timestamp settings. CDR & Carrier Statistics. | **Native editor, 33 B.** Verify the configured reporting windows and capture the related MT37 upload. |
| 33 | Modem parameters | Modem setup and link parameters. Call Setup / Interactive Interface. | **Native editor, 35 B.** Covers line timing, debounce and modem flags. The two-byte fixture is for storage tests. Verify transfer and effective settings on device. |
| 34 | Call and carrier statistics parameters | Summary and detail thresholds, upload windows, timestamps and enablement. CDR & Carrier Statistics. | **Native editor, 19 B.** Verify scheduling and correlate the summary and detail uploads. |
| 35 | Time and call-in parameters | Routine time of day, repeat and retry intervals, CDR threshold and one-time clock adjustment. Terminal Information / Call-ins. | **Native editor, 21 B.** Includes a default template and a link from the assigned phone. The NCC fixture acknowledged the edited record in the normal data set. Verify a table-triggered call and clock adjustment on device. |
| 49 | Answer supervision parameters | Polarity-reversal and wink timing, line control and number patterns. Call Setup. | **Native editor, 263 B for the inspected later layout.** The default draft covers the two documented timers and 16 empty patterns. The earlier 199 B import editor remains available. Verify target support, download and effective timers on device. |
| 50 | Coin validation | Coin windows, values and volumes, enable flags, cash-box and escrow thresholds. Coin/service configuration. | **Native editor, 105 B, Lab, exercised.** OEM install accepted three synthetic coin denominations. Verify calibrated values, rejection boundaries and in-service acceptance after rating. |
| 55 | Quick Access Keys / repertory dialer | Destination, upper/lower display text, key flags, call type and optional rate index. Quick Access Keys. | **Native editor, 571 B.** A draft template provides ten empty slots. Verify key behavior, rate references and model-specific slot counts. |
| 58 | Service Level Restrictions | Restriction level and activation window. Service Level Restrictions. | **Native editor, 26 B.** A draft template starts unrestricted. Verify restricted service, expiry and override behavior. |
| 59 / 92 | Call-Screening List | Dial patterns and wildcards, allow/deny/free flags, call type, answer supervision and rate index. MT59 is an older, smaller list. | **Native editors. MT92 Lab, 3,401 B, exercised.** The tested 200-slot profile routed the free call without MT63 when the free flag and rate index `0xFF` were both set. The editor supports keypad digits and unlimited trailing matches. Firmware review checks the free and direct rate indices on the tested profile. Verify priority, block and wildcard behavior and exercise the 60- and 180-entry profiles. |
| 62 | Numbering plan | Dial parsing and translation, digit format, international handling, digit timers and local dialing search mode. | **Native editor, 353 B, Lab.** A default-based payload loaded. The editor can author dialing and display-formatting rules, link prefixes and set currency formatting. Verify edited rules and currency settings on device. |
| 68 | Optional messaging service | Audio-Gram Delivery System settings. The help and firmware layout agree on switch access, call and payment permissions, surcharges, reporting and signaling fields. Service statistics upload separately as MT69. | **Native editor, 72 B.** Inactive template and editable access, reporting, permissions, surcharges, tones, firmware timers, and a write-only service key. Verify the legacy voice-prompt delay mapping and the switch dialogue on compatible firmware. |
| 72 | Spare table / electronic purse parameters | Fixed 1,000-byte payload with collection, settlement, transport, reload and alternate operator fields in the later layout. Spare Table. | **Native editor, 1,001 B.** Named settings cover the assigned portion. The byte grid supports fixed-offset edits and masks known protected fields. Verify the record and each enabled service on compatible firmware. |
| 73 | Terminal tariff and rate ladders | Charge and duration steps, rate classes and indexed tariff records. Rate Table. | **Native editor, 1,192 B, Lab.** The 128-entry profile has an unavailable-rate template and supports the unlimited-period sentinel in both steps. Verify terminal-computed offline rates and identify the layouts for other rate ladder variants. Host quote editing and MT63/64 are available separately. |
| 74-81, 90-91 | Uncompressed NPA/NXX local dialing segments 1-10 | Area-code/exchange search and rating classification data. Availability varies by firmware. | **Native editor, 819 B per segment.** Blank templates and the firmware-specific area code encoder are available. Verify a complete set of segments and cross-segment lookups on device. |
| 93 | Smart-card parameters | Validation slots, value limits, toll rebates and per-zone discount methods. Credit/Calling/Smart Cards. | **Native editors, 225 B.** An inactive Multi-pay record loaded in the lab. The editor separates the older and later field maps, and validation slots are write-only in the native API. Verify provider-supplied provisioning and processing on each compatible terminal. |
| 94 | Remote code download parameters | Code-server route, schedule, baud, authentication, target revisions, retries and the flash-sector bitmap. Download Code Scheduler. | **Native editor, 100 B.** The listener fixture verifies standalone delivery at routine call-in, acknowledgement and retention of the active data assignment. Verify MT94 delivery, terminal scheduling, code-server transfer and upgrade on compatible firmware. |
| 95 | Instant Win | Eligible smart-card serial ranges, application flags and display prompts. Instant Win. | **Native editor, 153 B.** A draft template provides ten disabled ranges and three empty prompts. Verify the promotion on compatible smart-card firmware. |
| 98 / 99 | Visual prompts, languages 3 and 4 | The 400-row editor also supports these records. Firmware and display hardware determine which language and glyphs are available. | **Native editor.** Establish target support, verify language selection and finish hardware-specific glyph authoring. |
| 101 | Compressed NPA/NXX local dialing segment 1 | First page of compressed local exchange/rating data. | **Native editor, 403 B, Lab.** A blank template is available. One segment loaded and a rated call reached the host. Verify edited paired exchange values, composite search, routing and offline behavior. |
| 102-115 | Compressed NPA/NXX segments 2-15 | Additional local dialing and classification pages. Support depends on firmware build. | **Native editor, 403 B per segment.** Verify segment count, ordering, boundary lookups and inter-page links. |
| 131-133 | Graphical terminal tables 1-3 | Graphic screen IDs, assets, text and mixed screens, rotation and linkage to call functions. Advertising Graphic. | **Native editor.** Bitmap, scrollable text and mixed panels, inactive templates, rotation and choice-key controls. Verify downloads and rendering on compatible graphical firmware. MT153 is a usage report. |
| 136-149, 154-155 | Double-compressed NPA/NXX segments 1-16 | Alternate compression profile with a separate type range and ordering. | **Native editor, 203 B per segment.** Start a blank template and edit four packed exchange indices per row. Verify device lookup across supported segment boundaries. |
| 150 | NPA set-based rating | Area-code-to-tariff-index lookup. NPA Rating. | **Native editor, 401 B.** Edit paired area-code rate codes with MT73 references 16 through 29 and the host and invalid options. Verify the lookup on device. |
| 151 | International set-based rating | Country/destination-to-tariff-index lookup. International Rating. | **Native editor, 604 B.** Configure the no-match action, default rate and up to 200 destination/rate pairs. The inspected firmware maps codes 2 through 99 to MT73 rows 30 through 127. The desktop help describes rows 30 through 91. Verify behavior and compatibility on device. |
| 152 | Holidays and time-of-day discounts | Calendar dates, weekday, Saturday, Sunday and holiday schedules, and payment eligibility by call zone. | **Native editor, 95 B.** A draft template provides ten holiday slots and eight periods with coin and card masks and references to the later MT93 discount rows. Verify dates, full-day and overnight periods, and the linked MT93 discounts on device. |

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

## Graphical screen formats

The inspected later graphical profile stores eight 1,152-byte screens after an eight-byte header in each MT131 through MT133 record. The screen kind selects how the same 1,044-byte body is used.

| Kind | Display fields | Authoring status |
| --- | --- | --- |
| Bitmap (`B`) | The first 1,008 body bytes hold 112 by 72 monochrome pixels. Screen attributes and six key actions follow the body. | Native artwork upload and monochrome preview. |
| Text (`T`) | The desktop help describes a nine-line screen with 19 cells per line. The firmware can scroll across 54 stored rows. A zero at the start of a row ends the displayed text. | Native multiline editing and nine-line preview with paging. |
| Mixed (`M`) | The body contains four 261-byte panels. Each starts with four 19-cell text rows and three 19-cell key-label rows, then its own attributes and six key actions. | Native panel text, key labels, attributes and separate key-action editing. |

## Manager data and workflows

| Legacy surface | Current CoinLine coverage | Work remaining |
| --- | --- | --- |
| Terminal Information, sites, customer ownership and Groups | Terminal, site and customer records. A phone can participate in up to 40 groups, and each membership contributes to group assignment previews. NCC identity is matched against the terminal's on-wire ID. | Complete the per-field legacy mapping, especially the split between inventory, firmware profile, and terminal parameters. |
| Configuration naming, version history and update by group | Generic definitions, draft copies, versioned sets, publish, and assignment or scheduled pickup for phone, group, location or customer. The console tracks schedule availability and individual delivery attempts. | Verify routine pickup on OEM firmware, immediate callback and per-device firmware eligibility. Group, location and customer schedules capture the eligible phones at submission. |
| Call Setup and Interactive Interface | Native access, feature, servicing, user-interface and modem records. MT26 and MT31 offer distinct older and later layouts at the same wire size. | Verify live changes and complete the mapping of unsupported build-specific flags and answer-supervision profiles. |
| Audio-Gram Delivery System | MT68 has a native editor for the identified fields. MT69 reports service statistics. Both correspond to the workflow described in the desktop help. | Resolve the legacy voice-prompt delay setting and verify delivery and reporting with a compatible switch service. |
| Inmate Feature Configuration and Inmate Setup | The desktop help identifies two downloads. The feature editor covers PIN entry strategy, call handling, voice prompts, disconnect warnings, recorder signaling and timers. Desktop code confirms those fields are sent to Manager. The help describes Inmate Setup as per-terminal data maintained in the separate Inmate application. | Identify the terminal record types and packed layouts using a matching firmware catalogue or NCC capture. The available desktop records establish the Manager interface. |
| CDRs, performance and carrier statistics, and field alarms | Alarm and maintenance replies have been demonstrated. The NCC fixture verifies dedicated call-detail requests, sequence receipts, and decoded CDRs and statistics in Uploads. | Verify the upload dialogue and statistics schedules on firmware and reconcile the stored reports against terminal totals. |
| Cards, payments and reconciliation | Card product/account, payment and reconciliation models and screens. The deployed business path is simulated. | Connect a real authorization and settlement service before enabling approvals. Validate the independent terminal CDR flow. |
| Users and administrative preferences | Operator accounts, roles and settings. | Compare full desktop authorization, workstation defaults and configuration privileges with the current web workflow. |

## Native editor coverage

The WebUI recognizes editable record profiles by wire type and length. It selects the matching layout for records verified on a registered target build. When MT26 or MT31 has no verified target, the editor opens the older profile and offers a later profile selector. Other equal-size layouts may require a selection before editing. An edit applies named fields to the existing record and retains surrounding bytes, bit flags, credentials and reserved regions. Quick-access, service restriction, promotion and discount profiles also have inactive draft templates. The table-set workflow supports review, publish and assignment without requiring a new deployment.

Phone inventory identifies the registered firmware build. A table set can target one registered version. Assignment previews, direct assignments, and NCC pickup check that version. The selectable MTR 1.9 Multi-pay profile covers eleven lab-acknowledged record lengths. The remaining builds and feature combinations need their own format verification. Firmware declarations and records with unverified lengths still need device evidence.

### Maximizer terminal type references

The phone inventory can store and filter on the terminal family described in the desktop help. These type numbers identify a family for comparing its required and optional table lists with the registered firmware. The phone's separate operating mode is configured in MT26 and reported in MT60.

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

An upgrade test against PostgreSQL preserved the existing phone, firmware and group data. The browser verified creating, editing, filtering and clearing a family, and showed the selected family in a table set assignment preview.

| Workflow | Available native fields | Work still required for full parity |
| --- | --- | --- |
| Terminal and call setup | Management access, terminal family inventory, terminal operating mode, card authorization options, coin call options, advertising and display switches, user-interface timers, modem and installation timing, answer supervision and call-in settings. | Complete firmware-specific option sets and exercise edited data on each compatible target. |
| Card, coin and carrier | Card ranges, standards, service codes and data spill, stored-value issuer and manufacturer rules, value limits, write-only key replacement, coin values and validator enablement, and per-carrier payment acceptance. | Verify product-specific stored-value provisioning, in-service payment acceptance and provider-backed authorization. |
| Dialing and rating | Quick-access keys, service restriction windows, call-screening patterns and payment types, linked dialing and number-formatting rules, currency, rate ladders, local dialing segments, NPA and international ratings, holidays and discounts. The verified Multi-pay profile checks standard free and direct tariff references and flags reliance on a previously loaded tariff record. | Extend cross-record review to additional profiles and service-specific references. Verify edited rules on device. |
| Advertising and display | Text advertising and named language messages with display previews, bitmap, text and mixed graphical screen editors, rotation, key destinations and promotion prompts. | Hardware-specific glyph authoring and device validation for graphical and language profiles. |
| Operations and optional services | Statistics reporting intervals, code download schedule, optional messaging, and named and fixed-offset spare-table editing for later e-purse and alternate operator settings. | Verify e-purse and alternate operator behavior on compatible firmware. Complete server-side Audio-Gram and inmate workflows and controlled validation of code download settings. |

The source check included the archived WinHelp project, RTF topics and screen references for call screening, quick-access keys, service levels, advertising, carrier screens and rating. CoinLine uses those workflows in its own interface. The archive confirms the desktop screen responsibilities and does not establish that each server setting has a terminal wire field.

## Runtime card and rating calls

An ordinary assigned install set transfers on MT44. A due scheduled download can also transfer on a routine MT8 call-in or MT9 callback. A runtime authorization or quote exchange returns its application reply after the NCC link ACK. The terminal can then acknowledge and clear the call.

| Runtime dialogue | Current coverage | Remaining work |
| --- | --- | --- |
| MT63 real-time rate request → MT64 | The published host plan supplies the quote. Prior OEM firmware accepted, displayed and cleared an actual quote. | Broader plan and firmware profiles remain to be exercised. |
| MT1 formatted card authorization → MT3, MT2 extended authorization → MT65 | The listener recognizes both formats and returns a protocol decline to matched terminals. Raw bytes from directly received authorization and card CDR records are excluded from listener session traces. | Obtain a provider integration and exercise both a decline and a provider-backed approval using a synthetic card in the emulator. |
| MT42 → MT17, then MT4/53/61 call details → MT5 | The listener starts a dedicated upload, stores complete CDRs from matched phones without card data, and groups sequence receipts for stored records. The NCC fixture covered all three layouts, reconnect and header-only retries, and truncated records. Two concurrent loopback sessions against PostgreSQL received receipts for one stored record. Uploads displays the decoded details. | Verify physical terminal acceptance, routine call-in collection and the CDRs appended to immediate-clear requests. |
| MT37, MT56, MT57, MT69 and MT71 statistics | The listener decodes complete reports at packed record boundaries and stores them before the link ACK. Uploads shows the terminal clock period, call and communications counters, carrier payment breakdowns, and optional messaging counts. The TCP fixture exercises all five report types, packed CDR and status prefixes, repeated carrier data and truncation. An isolated PostgreSQL run retained one MT57 report through a retry and closed unregistered and truncated attempts before ACK. The browser displayed both stored carriers and saved an operator review. | Verify report schedules and a complete upload on terminal firmware. Correlate MT37 timing buckets with the assigned MT32 interval settings. Decode the additional reboot counters only when a later MT37 layout can be identified. |

Terminal Status displays MT10 health flags and MT60 reported software versions as their own events. The upload fixture verifies both records within an MT42 request. In the inspected firmware, the MT60 mode byte reports the EEPROM card-only, Multi-pay or coin-only setting.

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
