# Protocol boundaries

| Layer | Responsibility |
|-------|------------------|
| **NccFrameCodec** | Encode/decode framing — strict vs diagnostic gap-preserving modes |
| **DlogPayloadClassifier** | Non-authoritative decode + diagnostics |
| **DlogMessageTypeRegistry** | Seeded MT names — gaps flagged |
| **Business services** | Never discard unknown raw bytes |
