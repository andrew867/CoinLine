# Table distribution

CoinLine stores table records as versioned binary payloads with SHA-256 hashes and lengths. Operators can edit supported layouts in the WebUI, import payloads, publish sets and assign them to terminals.

A matched MT44 starts transfer of the assigned published set. The host sends the MT18 header, paces fragments with link acknowledgments and records the terminal's MT14 result per table. The final MT13 acknowledgment completes the batch. Ten records completed this exchange in the firmware lab.

An available scheduled full data set is eligible on a matched MT8 call-in, MT9 callback or MT44 table request. The host retains the latest attempt and makes an interrupted schedule available for another call. Scripted NCC tests cover MT8 and MT9 pickup, including recovery across an abrupt API restart on PostgreSQL.

Scheduled record selections are delivered on MT8 or MT9. The host resolves the required records from the published source set, sends the selected records in dependency order and records the MT14 results before completing the schedule. Explicit MT44 requests use the normal full-set path when a partial schedule is waiting. If a full transfer has already started on the connection, the selection is available on a later call.

A special callback sends MT19 on supported management exchanges. These include maintenance and alarm replies, MT8, MT9, MT36 clock synchronization and MT44. Its six date and time fields are binary, with the year stored as years since 1900. CoinLine records the terminal's matching link acknowledgment. An acknowledged request remains open until a matched MT9 arrives at or after the requested time. The MT9 can be a new dialogue on the active connection or arrive on a later connection. A re-offer uses the same programmed time.

CoinLine offers a due scheduled set first on the return. If the operator also selected assigned-table delivery, the current compatible published assignment can be sent on that return. CoinLine skips a second table transfer on a connection that already started one. MT8 and MT9 end with MT13 when there is no table transfer. Card authorization and real-time rating use their dedicated replies without MT19 or table transfers.
