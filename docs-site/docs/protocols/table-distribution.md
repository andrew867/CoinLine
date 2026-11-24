# Table distribution

CoinLine stores table records as versioned binary payloads with SHA-256 hashes and lengths. Operators can edit supported layouts in the WebUI, import payloads, publish sets and assign them to terminals.

A matched MT44 can start transfer of a published set. The host sends the MT18 header, paces fragments with link acknowledgments and records the terminal's MT14 result per table. The final MT13 acknowledgment completes the batch. Ten records completed this exchange in the firmware lab.

The host prepares an ordered batch at the scheduled time. The first eligible call claims the prepared batch and records acknowledgments on its original ID. A full data schedule is eligible on a matched MT8 call-in, MT9 callback or MT44 table request. The host keeps interrupted attempts in download history and creates a new attempt on the next eligible call. Pending prepared batches survive a restart.

Scheduled record selections are delivered on MT8 or MT9. The host resolves the required records from the published source set, sends the selected records in dependency order and records the MT14 results before completing the schedule. A partial fleet schedule remains available for an eligible call while an MT44 request follows its normal selection rules.

The Downloads page also prepares a pending batch for a single phone. Eligible MT8, MT9 and MT44 calls pick the oldest pending batch after any eligible fleet schedule. The terminal's acknowledgements update the prepared rows under the original batch ID, and the batch retains the session ID. CoinLine permits one active table transfer per phone. When MT44 has no scheduled or pending batch, it uses the current published assignment.

A special callback sends MT19 on supported management exchanges. These include maintenance and alarm replies, MT8, MT9, MT36 clock synchronization and MT44. Its six date and time fields are binary, with the year stored as years since 1900. CoinLine records the terminal's matching link acknowledgment. An acknowledged request remains open until a matched MT9 arrives at or after the requested time. The MT9 can be a new dialogue on the active connection or arrive on a later connection. A re-offer uses the same programmed time.

CoinLine offers a due scheduled set first on the return. If the operator also selected assigned-table delivery, the current compatible published assignment can be sent on that return. CoinLine skips a second table transfer on a connection that already started one. MT8 and MT9 end with MT13 when there is no table transfer. Card authorization and real-time rating use their dedicated replies without MT19 or table transfers.
