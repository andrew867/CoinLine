#!/usr/bin/env bash
# Restore from pg_dump plain SQL. Drops connections first — use on maintenance window only.
set -euo pipefail
FILE="${1:?usage: restore-postgres.sh backup.sql}"
CONN="${PGURL:-postgresql://host:host@127.0.0.1:5432/hostplatform}"
echo "Restoring $FILE (destructive target database)"
psql "$CONN" -v ON_ERROR_STOP=1 -f "$FILE"
echo "Done."
