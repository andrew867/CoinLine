# DLOG (CoinLine Server)

- **Registry:** `DlogMessageTypeRegistry` — populated from **supported protocol interfaces**, **OEM compatibility specifications**, and **tables shipped with this product**. Rows should be treated as **draft until validated on supported terminal hardware** when expanding coverage.
- **Classifier:** `DlogPayloadClassifier` — diagnostics may flag **UNKNOWN_MT**, **EMPTY_PAYLOAD**, etc.
- **Storage** preserves raw payloads for protocol diagnostics. The live NCC listener omits raw bytes from directly received card authorization, card call detail and maintenance request frames. A maintenance event retains the opcode and omits the technician PIN.

The terminal status view shows decoded alarms (MT7), maintenance reports (MT6), known status flags (MT10), cash box reports and table or CDR request reasons. A field upload can contain multiple events. The listener reads each supported record in the upload and reports unlisted codes numerically.

### MT10 status masks and MT7 alarms

MT10 contains five independent status-mask bytes at offsets 6 through 10. Status index `8 × group + bit` identifies a mask position, with zero as the least significant bit. A status index is not necessarily an MT7 alarm number. The shared 27 positions are decoded for every profile. The verified earlier profile additionally identifies status-only operating error (13), nonfatal logic error (14) and fatal error/reboot (15). The later profile adds these ten conditions:

| Status index | Offset | Mask | Later meaning |
| --- | --- | --- | --- |
| 2 | 6 | `04` | EPM/SAM not responding |
| 3 | 6 | `08` | EPM locked out or serious module error |
| 4 | 6 | `10` | EPM expired |
| 5 | 6 | `20` | EPM/SAM approaching or reaching transaction limit |
| 6 | 6 | `40` | Primary collection system unreachable |
| 11 | 7 | `08` | Alternate collection system unreachable |
| 13 | 7 | `20` | Required configuration table missing |
| 14 | 7 | `40` | Data-jack port blocked |
| 37 | 10 | `20` | Collection upload dialogue failure |
| 38 | 10 | `40` | Code-download server connection failure |

The later wire meaning of index 15 is unverified. Indices 36 and 39 remain unresolved on both profiles. An unknown profile preserves reused indices as explicitly profile-dependent instead of assigning either meaning. The ten later conditions also have defined MT7 alarm codes; indices 7 and 15 are status-only, and alarm 99 is a separate fault-cleared event. No MT7 meaning is assigned to 36 or 39.

The cashbox ledger accepts the packed terminal MT38 status record of 57 bytes and MT51 collection record of 71 bytes. Reports are stored before the NCC link acknowledgement. MT51 also receives MT13. A reconnect replay matches the same phone and report fields, excluding reserved tail bytes. A combined collection can supersede an earlier report from the same collection while retaining the removed box details.

MT44 requests cashbox restoration with reason bit `0x80`. CoinLine returns the stored MT38 in the MT18 reply, with MT13 to close a status-only exchange. When MT44 also requests tables, the MT18 header carries the cashbox record before the regular transfer. A status-only call leaves downloads queued. After a collection, a new MT38 establishes the current balance for restoration. A status-only request without a current report receives MT13.

See companion reference materials under `docs/protocols/` in the public repository when published alongside terminal certification packs.
