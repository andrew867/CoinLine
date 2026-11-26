-- Run with psql against a backup-tested legacy database during a maintenance window.
-- A failure stops the script before the baseline marker is written; rerun after resolving the failure.
\set ON_ERROR_STOP on
\ir legacy/preflight.sql
\ir legacy/schema-before-card-privacy.sql
\ir legacy/card-privacy-upgrade.sql
\ir legacy/stamp-baseline.sql
