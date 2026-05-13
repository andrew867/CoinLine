# DLOG fixtures

| Fixture id | Binary | Lineage |
|------------|--------|---------|
| `dlog.empty_payload` | `empty_payload.bin` (0 bytes) | CANONICAL |
| `dlog.unknown_mt_ff01` | `unknown_mt_ff01.bin` | INFERRED (classifier unit behavior) |
| `dlog.hex_literal_3f00ab` | *(hex only)* | CANONICAL (`DlogHex` round-trip) |
| `dlog.correlation_pairs` | *(JSON manifest)* | PLACEHOLDER — must match `DlogCorrelationRules.CompatibilityPairs` (see `DlogCorrelationCatalogFixtureTests`) |

`mtrScope`: **BOTH** for payload spans; meaning of unknown MT depends on firmware variant (`UNKNOWN_MT` diagnostic).
