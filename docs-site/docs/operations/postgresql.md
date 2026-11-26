# PostgreSQL

CoinLine uses PostgreSQL through Npgsql and EF Core.

New databases are initialized by the active schema baseline when the API starts. Existing deployments with the superseded migration history must use the explicit legacy upgrade before updating the API. The upgrade preserves existing records and migration IDs, applies missing archived migrations, and then registers the baseline. It refuses unexpected or incomplete histories instead of treating them as a new database.

See the repository's `backend/database/README.md` for the backup, maintenance-window and upgrade procedure. Never delete the existing database volume to resolve a migration mismatch.
