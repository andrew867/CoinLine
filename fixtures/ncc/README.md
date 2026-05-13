# NCC wire fixtures

| Artifact | Lineage | Evidence |
|----------|---------|----------|
| `control_clr.bin` / `.hex` | CANONICAL (FixtureGen + codec) | `docs/protocols/ncc_framing/`, `NccFrameCodec.cs` |
| `message_min.bin` / `.hex` | CANONICAL | Same + `GoldenNccFixtureTests.cs` |

`mtrScope`: **BOTH** — framing follows vendor-oriented sources in-code; treat hardware timing as `HARDWARE_VALIDATION_REQUIRED` where docs note ambiguity.
