# Production deployment

1. TLS-terminated reverse proxy → API containers.
2. PostgreSQL HA external to API.
3. Static UI or CDN for React `dist/`.
4. Worker optional — heartbeat gate readiness.

Validate **`ValidateProductionSecurity`** throws if ApiKey mode missing keys.
