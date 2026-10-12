#!/usr/bin/env bash
set -euo pipefail

echo "=== PostgreSQL setup (native, no Docker) ==="

if ! command -v psql >/dev/null 2>&1; then
  echo "psql not found. Install PostgreSQL 16 via your distro packages, then re-run." >&2
  echo "  Ubuntu: sudo apt-get install -y postgresql postgresql-contrib" >&2
  exit 1
fi

if ! pg_isready >/dev/null 2>&1; then
  echo "PostgreSQL is not accepting connections. Start it (e.g. sudo systemctl start postgresql)." >&2
  exit 1
fi

DB_NAME="${CHANNELED_DB_NAME:-channeled}"
DB_USER="${CHANNELED_DB_USER:-channeled}"
DB_PASSWORD="${CHANNELED_DB_PASSWORD:-}"

if [[ -z "${DB_PASSWORD}" ]]; then
  echo "Set CHANNELED_DB_PASSWORD before running this script (do not commit secrets)." >&2
  exit 1
fi

sudo -u postgres psql -v ON_ERROR_STOP=1 <<SQL
SELECT 'ok' WHERE 1=1;
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${DB_USER}') THEN
    CREATE ROLE ${DB_USER} LOGIN PASSWORD '${DB_PASSWORD}';
  END IF;
END
\$\$;
ALTER ROLE ${DB_USER} CREATEDB;
SELECT 'CREATE DATABASE ${DB_NAME} OWNER ${DB_USER}'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '${DB_NAME}')\gexec
SELECT 'CREATE DATABASE ${DB_NAME}_test OWNER ${DB_USER}'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '${DB_NAME}_test')\gexec
SELECT 'CREATE DATABASE ${DB_NAME}_restore_drill OWNER ${DB_USER}'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '${DB_NAME}_restore_drill')\gexec
SQL

sudo -u postgres psql -v ON_ERROR_STOP=1 -d "${DB_NAME}" -c "CREATE EXTENSION IF NOT EXISTS pg_trgm;"
sudo -u postgres psql -v ON_ERROR_STOP=1 -d "${DB_NAME}_test" -c "CREATE EXTENSION IF NOT EXISTS pg_trgm;"
sudo -u postgres psql -v ON_ERROR_STOP=1 -d "${DB_NAME}_restore_drill" -c "CREATE EXTENSION IF NOT EXISTS pg_trgm;"

echo "PostgreSQL ready: database=${DB_NAME} (+ _test, _restore_drill) user=${DB_USER}"
echo "Connection string example:"
echo "  Host=localhost;Database=${DB_NAME};Username=${DB_USER};Password=***"
