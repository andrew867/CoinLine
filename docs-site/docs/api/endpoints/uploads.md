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

The NCC listener answers an MT42 upload request with MT17. After recording a complete CDR, it sends MT5 with the terminal's two-byte sequence. Receipts from the same incoming packet can share a reply packet. Idempotency uses the terminal identity and selected non-credential wire fields. A repeat call record receives the same sequence receipt without creating a second batch.
