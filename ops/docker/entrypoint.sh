#!/bin/sh
set -eu

# Railway (and most PaaS) inject PORT. Local compose uses 8080.
PORT="${PORT:-8080}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:${PORT}}"

# Ensure writable data dirs when present (ignore failures on read-only mounts).
for d in "${FileStore__RootPath:-/app/data/object-store}" \
         "${Backup__RootPath:-/app/data/backups}" \
         "${DataProtection__KeysPath:-/app/data/dp-keys}"; do
  if [ -n "$d" ]; then
    mkdir -p "$d" 2>/dev/null || true
  fi
done

# Fail fast with an actionable message (same requirement as the .NET resolver).
has_db=0
[ -n "${DATABASE_URL:-}" ] && has_db=1
[ -n "${DATABASE_PRIVATE_URL:-}" ] && has_db=1
[ -n "${ConnectionStrings__Postgres:-}" ] && has_db=1
[ -n "${PGHOST:-}" ] && [ -n "${PGUSER:-}" ] && [ -n "${PGDATABASE:-}" ] && has_db=1

if [ "$has_db" -eq 0 ]; then
  echo "channeled: no Postgres connection configured." >&2
  echo "Railway: open this service → Variables → New Variable:" >&2
  echo "  DATABASE_URL = \${{Postgres.DATABASE_URL}}" >&2
  echo "  (use the exact name of your Postgres service if it is not 'Postgres')" >&2
  echo "Then redeploy. Compose sets ConnectionStrings__Postgres automatically." >&2
  exit 1
fi

exec dotnet Channeled.Api.dll "$@"
