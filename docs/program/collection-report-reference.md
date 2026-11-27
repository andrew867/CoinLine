# Collection policy references on terminal uploads

## Review the configuration before downloading

The table-set detail and configuration setup wizard include a **Call record and statistics collection** review. It separately identifies MT34 and MT35 as included with a recognized layout, absent, or present with an unrecognized layout. Absence does not mean disabled or a zero threshold. A valid MT34 shows the statistics interval and count threshold, the carrier-statistics setting, the optional CDR mode, the optional threshold and the stored phone-local window. A valid MT35 shows the separate normal call-record threshold used outside that optional window.

The review identifies excluded local coin calls, inconsistent collection controls, unrecognized optional mode combinations and missing or nonpositive thresholds without predicting when a phone will call in. Operators with record access can open an included MT34 or MT35 directly from the saved set; pending editor changes prevent navigation. The setup preview shows the same values without altering the copied records. These are candidate settings only, not confirmation of downloaded, activated or historically effective configuration.

The review is covered by focused protocol and configuration tests, four real PostgreSQL integration tests, and browser component tests for the table-set and setup pages. The production Web build also passes.

## References after receipt

Call and carrier statistics reports (MT56, MT57 and MT71) now include a reference to the most recent retained terminal-acknowledged MT34 before the manager received the report. Call details also include the MT34 optional logging policy and the independently acknowledged MT35 normal record-count threshold.

The reference is selected for the specific phone from a successful application acknowledgement, with a source download link and the recorded acknowledgement time. Queued or unrelated-phone records, current assignments and acknowledgements after receipt are not substituted. A later transfer without a retained acknowledgement is explicitly identified as uncertain. Missing, incompatible, ambiguous and permission-restricted references have separate states.

The MT34 reference shows the stored collection schedule, statistics threshold, carrier enable, raw flags, and the optional CDR mode, window and threshold. The profile-dependent combined-summary bit is identified without inferring its meaning from record length. MT35 shows the normal upload threshold. No raw table bytes are returned by these references, and viewing them requires read access to Downloads and Table records in addition to access to the upload.

These are receipt-time references, not proof of historical collection settings. The phone's own clock, previous configurations or unobserved changes can affect a period. Actual report timestamps, counters, and call charges and collections remain unmodified.

## Independent earlier-profile collection qualification

An isolated earlier Multi-Pay configuration without MT34 or MT35 had no retained uploads after previous locally connected calls. A separate configuration preserved all eleven assigned record digests and added reviewed MT34 and MT35 records. The native editor selected completed-call optional logging, an optional count threshold of one and a normal count threshold of one, preserving the other collection settings. The terminal acknowledged all thirteen records during cold INSTALL and returned to service after restart.

One synthetic quarter paid the exact local call; the local CO remained answered for 20 emulated seconds before normal hook replacement. No CDR reached the Manager during a 180-second on-hook observation, although the terminal made a separate callback dialogue. Normal Craft call-record collection subsequently displayed **Call records present**, started the NCC call and finished with **Completed 00**. The Manager persisted one genuine MT53 with local classification, coins, an exact test destination, terminal-reported duration 21 seconds, 25 cents charged and 25 cents collected. The native diagnostic captured a CRC-matched MT5 receipt body with the same sequence and an EOD marker; its final delimiter was not retained in that diagnostic. Raw stored upload bytes were empty, and the terminal cleared and returned to service. A separate warm run from that saved post-upload state reached the same Craft option, displayed **No call records**, and the retained upload count remained one.

This establishes changed-policy call-record creation and ordinary manual upload on the earlier emulator. It does not establish automatic count-triggered upload, changed carrier-statistics counters, a card CDR or physical-terminal acceptance. No external call was made.
