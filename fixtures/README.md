# Host platform fixtures

Deterministic corpora for golden tests live here under **single-source paths** copied into test output (`fixtures\…` next to `HostPlatform.Tests.*` assemblies).

## Layout

| Directory | Contents |
|-----------|----------|
| `protocol/` | JSON Schema (`fixture-schema.json`) and protocol-level README |
| `ncc/` | NCC wire `.bin` / `.hex` (regenerate via `host-platform/backend/tools/FixtureGen`) and `.fixture.json` manifests |
| `dlog/` | DLOG classifier / hex round-trip examples |
| `tables/` | Opaque table distribution payload edge cases |
| `rating/` | Host-side `RatingEngine` JSON inputs (no wire bytes) |
| `cards/`, `craft/`, `firmware/` | Placeholder or integration-aligned metadata (see each `lineage.canonicality`) |
| `hw_validation/` | Sample **`schemaVersion`: 1** captured-session envelope (`sample_session_envelope_v1.json`) for import/replay CI — lineage **INFERRED**, not hardware captures |

## Regenerating NCC bytes

From the repository root:

```bash
dotnet run --project host-platform/backend/tools/FixtureGen -- host-platform/fixtures
```

## Authoritative protocol references

Repository `docs/protocols/` (for example `docs/protocols/ncc_framing/`, `docs/protocols/host_platform/dlog_tables/`) and generated tables under `docs/generated/` where applicable.

Tests: `HostPlatform.Tests.Golden` (manifest + NCC), `HostPlatform.Tests.Protocol` (DLOG), `HostPlatform.Tests.Unit` (tables, rating, placeholders).
