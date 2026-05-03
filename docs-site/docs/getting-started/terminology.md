# Terminology

| Term | Meaning |
|------|---------|
| **NCC** | Framed terminal↔host packetization (STX…ETX, CRC16, control vs message). |
| **DLOG** | Message-keyed operational logs (MT registry derived from firmware headers in-repo). |
| **MT** | DLOG message type (first octet when in-band). |
| **Table set** | Published bundle of table versions for download orchestration. |
| **Opaque payload** | Bytes stored as **blob + SHA-256** without inventing ROM/DAT layout in API. |
| **Diagnostic gap-preserving parse** | Relaxed NCC decode path retaining gaps **for inspection** — not interchange certification. |
| **HARDWARE_VALIDATION_REQUIRED** | Explicit gap — needs captures/tests/signed field acceptance. |
| **SIMULATION ONLY** | Host advances state without certified modem/flash/PCI live paths. |
