# Table distribution

CoinLine stores externally produced table records as versioned binary payloads with SHA-256 hashes and lengths. The API can import payloads, publish sets and assign them to terminals. Profile-specific field editing and semantic validation remain open.

A matched MT44 starts transfer of the assigned published set. The host sends the MT18 header, paces fragments with link acknowledgments and records the terminal's MT14 result per table. The final MT13 acknowledgment completes the batch. Ten records completed this exchange in the firmware lab.

An available scheduled set is eligible on a matched MT8 call-in, MT9 callback or MT44 table request. A matched MT8 or MT9 without a due set receives MT13. The host retains the latest attempt and makes an interrupted schedule available for another call. Card authorization and real-time rating have their own dialogues and never pick up a schedule. Scripted NCC tests cover MT8 and MT9 pickup, including recovery across an abrupt API restart on PostgreSQL.

Card authorization and real-time rating use their own request and reply sequences. They do not start an install transfer.
