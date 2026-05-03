#!/usr/bin/env bash
# Logical backup of hostplatform database (plain SQL). Requires pg_dump on PATH.
set -euo pipefail
OUT="${1:-hostplatform-backup-$(date -u +%Y%m%dT%H%M%SZ).sql}"
CONN="${PGURL:-postgresql://host:host@127.0.0.1:5432/hostplatform}"
echo "Writing $OUT"
pg_dump "$CONN" --no-owner --format=p --file="$OUT"
echo "Done."
