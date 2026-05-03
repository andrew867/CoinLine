# Contributor guide

1. **Branch** from main integration branch per team practice.
2. **Tests** — add/extend tests with fixtures; mark uncertain protocol behavior `HARDWARE_VALIDATION_REQUIRED`.
3. **Docs** — update `docs-site/` when changing operator-visible behavior or APIs.
4. **OpenAPI** — regenerate after controller changes (`docs-site/scripts/export-openapi.sh`).
5. **Protocol claims** — cite `docs/protocols/` sources or fixtures.

Code style: match existing C#/TS conventions; focused diffs preferred.
