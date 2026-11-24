# Authentication & authorization

| Mode | Behavior |
|------|----------|
| **Development** | Local tools can send legacy operator headers in Development or Testing. |
| **ApiKey** | When `Security:Mode=ApiKey`, send `X-API-Key` with protected requests and sign-in. |
| **Operator session** | Sign in with `POST /api/auth/login` and send the token as `Authorization: Bearer <token>`. |
| **RBAC** | Policies `RequireOperator`, `RequireTechnician`, `RequireAdmin` — see [Roles](../security/roles-permissions.md) |

`GET /api/auth/me` returns the signed-in operator. An administrator can manage accounts in People. `GET /api/auth/accounts` returns safe account fields to administrators, `403` to signed-in operators without administrator access, and `401` to callers without a valid operator session. The API key gates the deployment. The bearer token supplies the operator identity and role in production.

The API also checks the current enabled account and session generation on every bearer request. Changing a role, disabling or re-enabling an account, or changing its password revokes its previous sessions. A revoked bearer request receives `401` even if it also carries a valid deployment key. Key-only service requests retain their existing access and cannot supply a named administrator identity. Tokens issued by earlier releases remain valid for unchanged accounts after migration until they expire or the account generation changes.

The browser client sends the bearer token and optional API key only to same-origin API routes. It rejects redirects and clears both credentials and the cached display name on sign-out. Completing a password change signs the browser out so the new password can be used for the next session.

Older API builds could return `500` for People when authorization had to challenge the caller without a registered default authentication scheme. The console displays the API status and offers a retry when the account list fails to load.

Set `Security:OperatorTokenKey` to a deployment secret if operator sessions must survive API restarts or work across instances. A production API without a configured signing key generates one at startup. All instances sharing operator sessions must use the same signing key.

Some routes `[AllowAnonymous]` for read-only catalogs (e.g. firmware package list) — verify OpenAPI per operation.
