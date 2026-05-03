# Table distribution fixtures

Opaque bytes only — `TableDistributionService.ValidatePayload` does not interpret ROM/DAT layout.

| Fixture id | Notes |
|------------|-------|
| `tables.opaque_empty` | INFERRED — must fail with `EMPTY_PAYLOAD` |
| `tables.opaque_min` | INFERRED — minimal passing opaque blob |

See `docs/protocols/host_platform/dlog_tables/conventions_and_download.md` for table semantics elsewhere in the stack.
