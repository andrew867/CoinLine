# Authentication & authorization

- **ApiKeyGateMiddleware** — **`X-API-Key`** when configured.
- **Policies** — Operator / Technician / Admin minimum roles.

The browser console uses a validated operator token and, when the deployment requires it, the configured API key. Authenticated same-origin API requests never follow redirects with credentials. HTTP 401 for the current session clears the stored token, key and identity and returns to login; a rejected sign-in or HTTP 403 does not trigger that transition. Responses from an older session cannot invalidate a newer session.
