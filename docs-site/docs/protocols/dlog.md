# DLOG (CoinLine Server)

- **Registry:** `DlogMessageTypeRegistry` — populated from **supported protocol interfaces**, **OEM compatibility specifications**, and **tables shipped with this product**. Rows should be treated as **draft until validated on supported terminal hardware** when expanding coverage.
- **Classifier:** `DlogPayloadClassifier` — diagnostics may flag **UNKNOWN_MT**, **EMPTY_PAYLOAD**, etc.
- **Storage:** `RawPayload` is authoritative; decoded JSON is **non-authoritative** diagnostic material.

See companion reference materials under `docs/protocols/` in the public repository when published alongside terminal certification packs.
