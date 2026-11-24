# Authentication & authorization

| Mode | Behavior |
|------|----------|
| **Development** | `DevOperatorMiddleware` may inject operator headers for local UX |
| **ApiKey** | `X-API-Key` header — required when `Security:Mode=ApiKey` (production guard) |
| **Operator session** | Sign in with `POST /api/auth/login` and send the token as `Authorization: Bearer <token>`. |
| **RBAC** | Policies `RequireOperator`, `RequireTechnician`, `RequireAdmin` — see [Roles](../security/roles-permissions.md) |

`GET /api/auth/me` returns the signed-in operator. An administrator can manage operator accounts in the console's People section. The operator token identifies the caller and its role. In API key mode the API key gate still applies.

Some routes `[AllowAnonymous]` for read-only catalogs (e.g. firmware package list) — verify OpenAPI per operation.
