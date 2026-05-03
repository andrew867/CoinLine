# NCC framing

- **Codec:** `NccFrameCodec` — **strict** decode vs **gap-preserving diagnostic** decode paths for inspection; CRC-16 uses **in-product** generator tables aligned to supported integrations.
- **Gap preservation:** diagnostic modes retain ambiguous octets for engineering review — **not** a substitute for interchange certification without **field validation**.

Refer to [Compatibility overview](compatibility-overview.md) for customer-facing scope statements.
