# Collection policy references on terminal uploads

Call and carrier statistics reports (MT56, MT57 and MT71) now include a reference to the most recent retained terminal-acknowledged MT34 before the manager received the report. Call details also include the MT34 optional logging policy and the independently acknowledged MT35 normal record-count threshold.

The reference is selected for the specific phone from a successful application acknowledgement, with a source download link and the recorded acknowledgement time. Queued or unrelated-phone records, current assignments and acknowledgements after receipt are not substituted. A later transfer without a retained acknowledgement is explicitly identified as uncertain. Missing, incompatible, ambiguous and permission-restricted references have separate states.

The MT34 reference shows the stored collection schedule, statistics threshold, carrier enable, raw flags, and the optional CDR mode, window and threshold. The profile-dependent combined-summary bit is identified without inferring its meaning from record length. MT35 shows the normal upload threshold. No raw table bytes are returned by these references, and viewing them requires read access to Downloads and Table records in addition to access to the upload.

These are receipt-time references, not proof of historical collection settings. The phone's own clock, previous configurations or unobserved changes can affect a period. Actual report timestamps, counters, and call charges and collections remain unmodified. A changed-policy terminal acceptance run is still necessary to prove the effect of the configured window.
