# DLOG (CoinLine Server)

- **Registry:** `DlogMessageTypeRegistry` — populated from **supported protocol interfaces**, **OEM compatibility specifications**, and **tables shipped with this product**. Rows should be treated as **draft until validated on supported terminal hardware** when expanding coverage.
- **Classifier:** `DlogPayloadClassifier` — diagnostics may flag **UNKNOWN_MT**, **EMPTY_PAYLOAD**, etc.
- **Storage** preserves raw payloads for protocol diagnostics. The live NCC listener omits raw bytes from directly received card authorization, card call detail and maintenance request frames. A maintenance event retains the opcode and omits the technician PIN.

The terminal status view shows decoded alarms (MT7), maintenance reports (MT6), known status flags (MT10), cash box reports and table or CDR request reasons. A field upload can contain multiple events. The listener reads each supported record in the upload and reports unlisted codes numerically.

See companion reference materials under `docs/protocols/` in the public repository when published alongside terminal certification packs.
