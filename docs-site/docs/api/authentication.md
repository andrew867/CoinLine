# Authentication & authorization

| Mode | Behavior |
|------|----------|
| **Development** | `DevOperatorMiddleware` may inject operator headers for local UX |
| **ApiKey** | `X-API-Key` header — required when `Security:Mode=ApiKey` (production guard) |
| **RBAC** | Policies `RequireOperator`, `RequireTechnician`, `RequireAdmin` — see [Roles](../security/roles-permissions.md) |

Some routes `[AllowAnonymous]` for read-only catalogs (e.g. firmware package list) — verify OpenAPI per operation.
