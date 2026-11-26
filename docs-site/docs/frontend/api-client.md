# API client

`coinline/web/src/api/client.ts` centralizes `fetch` to `/api` with optional API key header from env.

All environments: sign in for an operator token, use the same-origin API proxy, and include `X-API-Key` when `Security:Mode=ApiKey`.
