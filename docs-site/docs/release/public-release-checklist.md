# Public release checklist

Use before declaring a **public** or **customer-facing** release of documentation + artifacts. Items are **honest gates**: unchecked areas mean the release is **not** fully enterprise-certified.

## Current package: v0.2.0

**Published:** 2026-05-13 (documentation-aligned; GitHub: [CoinLine](https://github.com/andrew867/CoinLine)). Brings the repository to the same engineering snapshot as the [CoinLine Terminal Emulator](https://github.com/andrew867/CoinLine-emu) companion project.

**Authoritative package notes** — scope, honesty boundaries, demo workflow, next tranche:

- **[Changelog — v0.1.0](changelog.md#release-010)** (full release summary)
- **[Known limitations](../reference/known-limitations.md)** · **[Open questions](../reference/open-questions.md)** · **[Support policy](support-policy.md)**

This release is **not** production-ready for billing, certified payments, or uncertified field deployment without customer-specific validation. Sign-off below means **accurate labeling**, not fitness for a regulated production cutover.

## Documentation

- [ ] **OpenAPI regenerated** — `docs-site/docs/api/openapi/coinline.openapi.{json,yaml}` committed; diff reviewed against **`dotnet swagger tofile`** output
- [ ] **OpenAPI gap acknowledged** — checklist signer confirms awareness that **`/ready`**, **`/health/ready`**, **`/metrics`** may still be **missing** from the static file ([OpenAPI](../api/openapi.md), [Health](../api/endpoints/health.md)); probes validated against **running** `/swagger` or integration tests
- [ ] **MkDocs strict build passes** (`mkdocs build --strict` from `docs-site/`)
- [ ] **Every controller area** has narrative coverage ([Endpoints](../api/overview.md)) — **exceptions listed in PR** with rationale
- [ ] **Environment variables** documented ([reference](../reference/environment-variables.md) + repo secrets doc)
- [ ] **Entity reference** matches `HostPlatformDbContext` DbSets ([entity reference](../reference/entity-reference.md))
- [ ] **Background workers** documented ([background workers](../backend/background-workers.md))
- [ ] **Database migrations** listed with upgrade notes ([migrations](../backend/migrations.md))
- [ ] **Deployment** path documented ([production deployment](../operations/production-deployment.md))
- [ ] **Backup/restore** documented ([backups](../operations/backups-restore.md))
- [ ] **Security risks** summarized ([threat model](../security/threat-model.md), [limitations](../reference/known-limitations.md))
- [ ] **Hardware validation gaps** explicit ([protocols](../protocols/hardware-validation-required.md), [field validation](../field-validation/overview.md))
- [ ] **Dangerous operations** (replay export, flash, destructive deletes, retention trim) cross-linked ([dangerous operations](../security/dangerous-operations.md))
- [ ] **Simulation defaults** called out (cards, craft, firmware gates)
- [ ] **License/support** pages populated ([license](license.md), [support policy](support-policy.md))
- [ ] **Screenshots** optional — if absent, **TODO SCREENSHOT** placeholders exist with page/action
- [ ] **0.1.0-alpha package notes** reviewed — [changelog](changelog.md) release summary matches shipped behavior and marketing claims stay conservative

## Engineering gates

- [ ] `dotnet test` (backend) green
- [ ] `npm test` / `npm run build` (web) green
- [ ] Playwright E2E green where configured (CI resource permitting)

## Product / compliance sign-off

- [ ] PCI scope statement reviewed for card-touching deployments
- [ ] Field pilot acceptance criteria aligned with **SIMULATION ONLY** defaults and **`HARDWARE VALIDATION REQUIRED`** banners
