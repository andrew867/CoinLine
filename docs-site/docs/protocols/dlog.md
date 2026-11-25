# DLOG (CoinLine Server)

- **Registry:** `DlogMessageTypeRegistry` — populated from **supported protocol interfaces**, **OEM compatibility specifications**, and **tables shipped with this product**. Rows should be treated as **draft until validated on supported terminal hardware** when expanding coverage.
- **Classifier:** `DlogPayloadClassifier` — diagnostics may flag **UNKNOWN_MT**, **EMPTY_PAYLOAD**, etc.
- **Storage** preserves raw payloads for protocol diagnostics. The live NCC listener omits raw bytes from directly received card authorization, card call detail and maintenance request frames. A maintenance event retains the opcode and omits the technician PIN.

The terminal status view shows decoded alarms (MT7), maintenance reports (MT6), known status flags (MT10), cash box reports and table or CDR request reasons. A field upload can contain multiple events. The listener reads each supported record in the upload and reports unlisted codes numerically.

The cashbox ledger accepts the packed terminal MT38 status record of 57 bytes and MT51 collection record of 71 bytes. Reports are stored before the NCC link acknowledgement. MT51 also receives MT13. A reconnect replay matches the same phone and report fields, excluding reserved tail bytes. A combined collection can supersede an earlier report from the same collection while retaining the removed box details.

MT44 requests cashbox restoration with reason bit `0x80`. CoinLine returns the stored MT38 in the MT18 reply, with MT13 to close a status-only exchange. When MT44 also requests tables, the MT18 header carries the cashbox record before the regular transfer. A status-only call leaves downloads queued. After a collection, a new MT38 establishes the current balance for restoration. A status-only request without a current report receives MT13.

See companion reference materials under `docs/protocols/` in the public repository when published alongside terminal certification packs.
