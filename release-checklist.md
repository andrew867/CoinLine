# CoinLine — repository release checklist

Use before tagging a **public** or **customer-facing** drop of this repository.

## Branding

- [ ] [reference/branding.md](docs-site/docs/reference/branding.md) placeholders reviewed (`docs_url`, `public_repo_url`, support placeholders).
- [ ] No internal codenames in customer-facing PDFs or static exports.

## Engineering

- [ ] `cd backend && dotnet build && dotnet test`
- [ ] `cd web && npm ci && npm run build && npm run test` (and Playwright if required by your gate)
- [ ] `cd docker && docker compose build` (optional smoke `docker compose up`)

## Documentation

- [ ] `cd docs-site && mkdocs build -f mkdocs.yml -d site --strict`
- [ ] OpenAPI regenerated: `docs-site/scripts/export-openapi.ps1` or `.sh` → `coinline.openapi.{json,yaml}` committed

## Public documentation hygiene

Run the sanitizer from the **repository root**:

```bash
bash tools/public-docs-sanitize-check.sh
```

- [ ] Sanitizer exits **0** (no forbidden customer-facing phrases in scoped paths).

## Policy & legal

- [ ] [LICENSE.md](LICENSE.md) present and current.
- [ ] [Support policy](docs-site/docs/release/support-policy.md) matches your organization’s commitments.
- [ ] [Known limitations](docs-site/docs/reference/known-limitations.md) reviewed for honesty.

## Safety

- [ ] Dangerous operations documented with confirmations ([dangerous operations](docs-site/docs/security/dangerous-operations.md)).
- [ ] Security overview complete ([security overview](docs-site/docs/security/security-overview.md)).

## Forbidden public phrasing (must not appear in customer docs)

Examples the sanitizer enforces: “reverse engineering”, “decompiled”, “IDA”, “Hex-Rays”, “firmware source”, “private firmware repo”, “archaeology”, “lost source” — see script allowlist for tooling-only exceptions.
