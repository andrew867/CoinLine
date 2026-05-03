# Frontend overview

- **Stack:** React 18, TypeScript, Vite, React Router.
- **API:** `src/api/client.ts` — fetch helpers with relative `/api` base (dev proxy).
- **State:** Component-local + hooks; no global Redux requirement.

!!! note "IMPLEMENTED"
    Operator UI routes are summarized in [Routes](routes.md) and audited in [Route catalog](route-catalog.md) (every path + APIs).
