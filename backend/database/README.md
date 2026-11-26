# CoinLine database baseline

The active EF Core migration chain starts with one generated baseline. New databases use the normal API startup migration path. Do not apply the legacy scripts to an empty database.

## Existing databases

The archive contains all 43 superseded idempotent SQL migrations, including the terminal identity, alarm destination and card-data minimization upgrades. Existing migration IDs are retained for provenance; the new baseline marker is added only after the archived chain completes. No table is rebuilt solely to squash migration history.

1. Confirm the database and schema correspond to the intended CoinLine installation. Stop application and worker writers for the maintenance window. Complete and test a backup before modifying the database.
2. Run the upgrade using PostgreSQL's `psql` client, with the credentials supplied securely and the intended database selected. From the repository root:

   ```bash
   psql -X --host "$COINLINE_DB_HOST" --dbname "$COINLINE_DB_NAME" --username "$COINLINE_DB_USER" --file backend/database/upgrade-legacy.sql
   ```

   The default target is the connection's current schema. Confirm and configure the search path explicitly if the installation uses a non-default schema.
3. The script rejects an empty, unknown or non-contiguous legacy migration history. It applies only pending archived migrations, then registers the baseline. A failure stops the script before registration. Resolve the failure against the tested backup before retrying; do not manually mark a partial upgrade as complete.
4. Confirm the baseline marker exists in `__EFMigrationsHistory`. Start the API using the documented signing key and named operator configuration. The startup guard refuses to run a legacy migration history that has not been upgraded.

The archive contains schema and migration operations, not customer records. Card observation payload columns and untrusted manual transaction details are logically removed during the privacy upgrade. Historical backup retention and physical media sanitization are separate deployment responsibilities.

## Maintenance

Keep the legacy archive for installations that have not yet crossed the baseline. Generate subsequent migrations against the active baseline snapshot. The release baseline and its archive must reflect every integrated schema change; regenerate and reverify the consolidation if schema-changing work is merged before the baseline release.
