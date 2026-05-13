# API client

`coinline/web/src/api/client.ts` centralizes `fetch` to `/api` with optional API key header from env.

Production: configure proxy or same-origin and **`X-API-Key`** when `Security:Mode=ApiKey`.
