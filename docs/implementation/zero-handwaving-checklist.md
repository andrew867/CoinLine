# Zero-handwaving implementation checklist (CoinLine)

Use this before merging protocol, API, or operator-facing features.

## Production code paths

- [ ] No placeholder classes masquerading as production services (replace with real orchestration or delete).
- [ ] No `TODO` in production paths without a tracked compatibility-validation item and test or issue reference.
- [ ] No `throw new NotImplementedException` / `NotSupportedException` used as permanent stubs for operator-visible behavior.
- [ ] Undocumented HTTP endpoints: every route appears in OpenAPI with summary, request/response shape, and error cases.

## Protocols and parsers

- [ ] Every parser has malformed-input tests (strict rejection or diagnostic capture per mode).
- [ ] Raw bytes are preserved or explicitly accounted for (gaps, truncation, unknown payloads).
- [ ] Permissive / diagnostic capture mode never silently discards octets.

## State machines

- [ ] Explicit states and legal transitions; transition unit tests.
- [ ] Failure, retry, cancel, and audit hooks defined where operators rely on them.

## Dangerous operations

- [ ] Destructive or fleet-impacting actions require confirmation (query/body flag) and audit events.

## Verification commands (before claiming a tranche complete)

- `dotnet build backend/HostPlatform.sln`
- `dotnet test backend/HostPlatform.sln`
- `cd web && npm ci && npm run build && npm run test`
- `mkdocs build` for `docs-site` when documentation changed
