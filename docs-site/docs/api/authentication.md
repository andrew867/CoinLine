# Authentication & authorization

| Mode | Behavior |
|------|----------|
| **Development** | Local tools can send legacy operator headers in Development or Testing. |
| **ApiKey** | When `Security:Mode=ApiKey`, send `X-API-Key` with protected requests and sign-in. |
| **Operator session** | Sign in with `POST /api/auth/login` and send the token as `Authorization: Bearer <token>`. |
| **RBAC** | Policies `RequireOperator`, `RequireTechnician`, `RequireAdmin` — see [Roles](../security/roles-permissions.md) |

`GET /api/auth/me` returns the signed-in operator. An administrator can manage accounts in People. `GET /api/auth/accounts` returns safe account fields to administrators, `403` to signed-in operators without administrator access, and `401` to callers without a valid operator session. The API key gates the deployment. The bearer token supplies the operator identity and role in production.

The browser client sends the bearer token and optional API key only to same-origin API routes. It rejects redirects and clears both credentials and the cached display name on sign-out.

Set `Security:OperatorTokenKey` to a deployment secret if operator sessions must survive API restarts or work across instances. A production API without a configured signing key generates one at startup. All instances sharing operator sessions must use the same signing key.

Some routes `[AllowAnonymous]` for read-only catalogs (e.g. firmware package list) — verify OpenAPI per operation.
