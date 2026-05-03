# Protocol fixture manifests

Each `.fixture.json` file describes one golden artifact: lineage (canonicality and terminal generation scope), repository evidence paths, optional wire hex/binary linkage, and expected parse or diagnostic outcomes.

- JSON Schema: `fixture-schema.json`
- Shared corpora root: `host-platform/fixtures/`

Tests validate manifests under `HostPlatform.Tests.Golden` (contract + hex/binary parity) and domain-specific parsers under Protocol / Unit projects.
