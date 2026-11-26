# Uploads API

| Method | Path | Operation |
| --- | --- | --- |
| GET | `/api/uploads` | List terminal and imported upload batches |
| GET | `/api/uploads/{id}` | Read batch, metadata, and decoded records |
| POST | `/api/uploads` | Create a manual import |
| POST | `/api/uploads/{id}/ingest` | Decode a manual import |
| POST | `/api/uploads/{id}/reprocess?confirm=true` | Replace the derived records of a manual import |
| POST | `/api/uploads/{id}/operator-review` | Save an operator review note |

The live listener creates a completed batch for each supported MT4, MT53 or MT61 call detail from a matched phone. Such batches have `isTerminalCallDetail: true`. Their records return a typed `terminalCall` object and an empty `rawPayloadHex`. The metadata records the source NCC session. The `ncc-call-detail:` idempotency key prefix is reserved for live records. Ingest and reprocess return HTTP 409 for those batches.

An explicit terminal MT42 attention opens a call-upload dialogue by receiving MT17 from the manager. The manager commits supported call details, statistics and status records before acknowledging their frame, and sends MT5 for the call sequences. At a recognized final MT13 boundary it also returns application-level MT13, including when the terminal has only a status/version trailer left. The phone can then clear the link. An incomplete or unrecognized record does not complete the dialogue. Retry acknowledgements do not duplicate call details or statistics. This workflow neither picks up nor creates a table download.

The NCC listener answers an MT42 upload request with MT17. After recording a complete CDR, it sends MT5 with the terminal's two-byte sequence. Receipts from the same incoming packet can share a reply packet. Idempotency uses the terminal identity and selected non-credential wire fields. A repeat call record receives the same sequence receipt without creating a second batch.

Statistics reports use the reserved `ncc-statistics:` prefix and return `isTerminalStatistics: true`. Each batch record includes a typed `terminalStatistics` object with the record type, phone-local period, counter groups, and any carrier references. The raw payload is empty. MT37 communications, MT56 call counts, MT57 and MT71 carrier counts, and MT69 messaging have separate report layouts. Multiple fragments for the same carrier reporting period appear as separate reports with the same period.

For MT37, the detail response also includes `performanceReference`. Its status is `acknowledged`, `missing`, `ambiguous`, `invalid` or `restricted`. A selected retained acknowledgement supplies `downloadBatchId`, `tableRevision`, `ackRecordedAtUtc`, and `laterUncertainAttempt`. The supported MT32 layout returns four signed 10 ms timing pairs as `timing`, identified by measurement indices 0, 1, 3 and 4; the reserved pair is excluded. The lookup uses the same phone and the latest retained successful MT14 item at or before the host upload receipt. It does not read the current assignment or require that the entire source batch succeeded. The reference is not proof of the configuration during the phone-local reporting period. Callers without both Downloads and Table records read permission receive `restricted` without reference data. Other report types have no timing reference.

CoinLine stores complete statistics before sending the NCC link ACK. Those reports have no MT5 receipt. A truncated report or a storage failure closes the link without its ACK. Retries are matched by phone and report contents. Unused padding is ignored. Ingest and reprocess return HTTP 409 for live statistics records.
