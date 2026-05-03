# Error handling

- API returns problem details / JSON errors per endpoint.
- Destructive operations require **`confirm=true`** query or JSON flags (tables, firmware cancel, DLOG replay export, etc.).
- Transient DB retries in firmware orchestration (`PersistenceRetry`).
