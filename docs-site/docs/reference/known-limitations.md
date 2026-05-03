# Known limitations

**Release:** These limitations apply to **v0.1.0** and later until superseded in [Changelog](../release/changelog.md#release-010). For the full **package notes**, start there.

These items are **explicit** so customers do not infer completeness from UI polish or passing CI alone.

## Protocol & hardware

1. **Live field programming / modem transport** — orchestration exists; physical programming paths are **not** universally certified for all deployment models (**`Firmware:AllowLiveFlashing`** defaults **false**; requires **field validation** when enabled).
2. **Terminal download completion** — host records download intent; terminal-side completion signals may be **partially modeled** per integration.
3. **DLOG / NCC registries** — registry rows follow **OEM compatibility specifications** and in-product tables; unclassified message types may still appear until validated on **supported terminal hardware** (**UNKNOWN_MT**, diagnostics).

## Product domains

4. **Rating** — MVP semantics; unknown prefixes may be denied; set/table-rated constraints apply until customer test matrices are locked.
5. **Craft** — live modem execution for craft commands is not enabled in all builds — treat as **workflow and audit** until your program extends transport.
6. **Cards & reconciliation** — simulation-oriented defaults; treat as **ledger administration** under your **PCI** scope, not a turnkey acquirer integration.

## API & integration artifacts

7. **OpenAPI export** — static **`coinline.openapi.json`** may omit **`/ready`**, **`/health/ready`**, **`/metrics`**. Use live Swagger and [Health](../api/endpoints/health.md) for probes.
8. **OpenAPI schemas** — some projections are minimal — use narrative endpoint docs and live Swagger.

## Operations

9. **Seed / demo data** — optional startup seed can populate sample tenants — disable for strict production posture (`Platform:Seed:EnableDemoData`).
10. **Worker readiness** — **subsystem heartbeat** staleness can fail readiness when configured — see [Observability](../backend/observability.md).
