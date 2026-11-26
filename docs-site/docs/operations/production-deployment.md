# Production deployment

1. TLS-terminated reverse proxy → API containers.
2. PostgreSQL HA external to API.
3. Static UI or CDN for React `dist/`.
4. Worker optional — heartbeat gate readiness.

Set a unique signing secret (`COINLINE_OPERATOR_TOKEN_KEY`) before starting and provision a named administrator. `ValidateStartupSecurity` rejects a missing or invalid signing key and ApiKey mode without deployment keys. See [Authentication](../api/authentication.md).
