# Authentication & authorization

| Mode | Behavior |
|------|----------|
| **Development** | Local tools can send legacy operator headers in Development or Testing. |
| **ApiKey** | When `Security:Mode=ApiKey`, send `X-API-Key` with protected requests and sign-in. |
| **Operator session** | Sign in with `POST /api/auth/login` and send the token as `Authorization: Bearer <token>`. |
| **RBAC** | Policies `RequireOperator`, `RequireTechnician`, `RequireAdmin` — see [Roles](../security/roles-permissions.md) |

`GET /api/auth/me` returns the signed-in operator. An administrator can manage accounts in People. `GET /api/auth/accounts` returns safe account fields to administrators, `403` to signed-in operators without administrator access, and `401` to callers without a valid operator session. The API key gates the deployment. The bearer token supplies the operator identity and role in production.

The API also checks the current enabled account and session generation on every bearer request. Changing a role, disabling or re-enabling an account, or changing its password revokes its previous sessions. A revoked bearer request receives `401` even if it also carries a valid deployment key. Key-only service requests retain their existing access and cannot supply a named administrator identity. Tokens issued by earlier releases remain valid for unchanged accounts after migration until they expire or the account generation changes.

The browser client sends the bearer token and optional API key only to same-origin API routes. It rejects redirects and clears both credentials and the cached identity and permission profile on sign-out. Completing a password change signs the browser out so the new password can be used for the next session.

## Screen permission profiles

Administrators can open People, select Permissions, and choose role defaults or a custom profile. Custom profiles restrict access by screen and operation; they never grant a higher role. Supported operations are Read (1), Update (2), Add (4), and Delete (8). Unsupported operations are not editable. A blank custom profile denies all mapped management screens. Role defaults retain the account's existing access.

- `GET /api/auth/permission-catalog` lists the supported screens and their operation masks.
- `GET /api/auth/accounts/{id}/permissions` returns the account's profile, current role and revision.
- `PUT /api/auth/accounts/{id}/permissions` accepts the observed revision and a dictionary of screen keys and integer operation masks. Send `permissions: null` to restore role defaults; send an empty object to deny all screen access.
- Sign-in and `GET /api/auth/me` include the effective profile. Account-list responses do not contain the profile or session generation.

Profile changes revoke earlier sessions for the affected account. Unknown operations are rejected and stale revisions return `409`. The API enforces permissions independently of the menu and existing role policies still apply. Administrator accounts retain full role privileges. Deployment-key-only service clients retain their existing access.

Phone group administration and membership are separate permissions. Membership Read permits a minimal group lookup at `GET /api/terminal-groups/membership-options` and reading membership records, without access to the full group administration list. Add and Delete membership grants permit only the corresponding changes; changing the primary group without changing the membership set requires both.

Older API builds could return `500` for People when authorization had to challenge the caller without a registered default authentication scheme. The console displays the API status and offers a retry when the account list fails to load.

Set `Security:OperatorTokenKey` to a deployment secret if operator sessions must survive API restarts or work across instances. A production API without a configured signing key generates one at startup. All instances sharing operator sessions must use the same signing key.

Some routes `[AllowAnonymous]` for read-only catalogs (e.g. firmware package list) — verify OpenAPI per operation.
