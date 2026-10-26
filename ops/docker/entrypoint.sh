#!/bin/sh
set -eu

# Railway (and most PaaS) inject PORT. Local compose uses 8080.
PORT="${PORT:-8080}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:${PORT}}"

# Ensure writable data dirs when present (ignore failures on read-only mounts).
for d in "${FileStore__RootPath:-/app/data/object-store}" \
         "${Backup__RootPath:-/app/data/backups}" \
         "${DataProtection__KeysPath:-}"; do
  if [ -n "$d" ]; then
    mkdir -p "$d" 2>/dev/null || true
  fi
done

exec dotnet Channeled.Api.dll "$@"
