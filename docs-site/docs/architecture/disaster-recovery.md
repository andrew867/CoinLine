# Disaster recovery

1. **Database**: restore from backup (see [Backups & restore](../operations/backups-restore.md)).
2. **Secrets**: restore API keys / connection strings from vault.
3. **Firmware artifacts**: re-verify SHA-256 against registry after restore.
4. **Audit**: `AuditEvents` restored with DB — integrity depends on DB backup cadence.

RPO/RTO are **organization-defined** — not enforced by software defaults.
