# Explicit call upload completion

A running lab phone reported dialogue error 24 after uploading rated-call records. Redacted manager evidence showed MT42 followed by MT17, two stored MT53 call records and their receipts, then four carrier-statistics frames and a communications-statistics frame. The link receive deadline expired without a final host application reply. Two subsequent connections sent the exact MT10, MT60 and MT13 record sequence after MT17, received only the link acknowledgement, and reached the same deadline.

The explicit upload handler now waits for a final MT13 at a recognized record boundary. It persists supported call details, statistics, cashbox and status records before acknowledging the frame. It returns MT5 sequence receipts as appropriate and returns MT13 at completion, also when there are no remaining call details. An unknown or incomplete record does not receive a completion or link ACK in this dialogue. Raw explicit upload frame contents are omitted from the session capture. Direct unsolicited call-detail records retain their existing receipt-only behavior.

The completion response uses the same packet ID for a terminal retry, including a header-only retransmission. Call details and statistics retain their existing per-phone deduplication. The upload does not select a queued table batch. Runtime rating and authorization are separate dialogues.

Verification uses synthetic records with the observed frame sizes and record-type sequence, not retained raw customer data. Compatible terminal confirmation of a clean upload after the fix remains a separate acceptance gate.

The isolated PostgreSQL TCP regression saved two MT53 records and six statistics reports, returned both MT5 receipts, sent MT13 after the final MT37 report, replayed the same completion on full-frame and header-only retries, and completed a later status-only upload. Replaying the complete upload did not increase the stored call or statistics counts. The pending table batch stayed pending. The protocol tests also verified a single-frame upload whose MT5 receipts and MT13 share a reply and rejection of incomplete record boundaries.
