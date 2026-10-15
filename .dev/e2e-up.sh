#!/usr/bin/env bash
# Dev helper (not in repo): (re)start API + vite preview with the e2e environment and leave them running.
set -euo pipefail
ROOT=/home/jawarneh/Channeled
cd "$ROOT"
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=5432;Database=channeled;Username=channeled;Password=channeled_dev"
export FileStore__Kind=FileSystem
export FileStore__RootPath="$ROOT/.data/e2e-object-store"
export Backup__RootPath="$ROOT/.data/e2e-backups"
export DisableBackgroundJobs=true
export MigrateOnStartup=true
export RateLimits__AuthPermitPerMinute=100000
export Chat__PostsPerMinutePerUser=100000
export Downloads__PerStreamBytesPerSecond=2097152
export E2E_ADMIN_EMAIL=e2e-admin@example.com
export E2E_ADMIN_PASSWORD='E2eAdminPassword1!'
for extra in "$@"; do export "$extra"; done
mkdir -p "$FileStore__RootPath" "$Backup__RootPath"

pkill -f "Channeled.Api" 2>/dev/null || true
pkill -f "vite preview" 2>/dev/null || true
sleep 1

if [[ "${SKIP_API_BUILD:-0}" != "1" ]]; then
  dotnet build src/Channeled.Api -c Release --nologo -v q
fi
dotnet run --project src/Channeled.Api -c Release --no-build -- ensure-user \
  --email "$E2E_ADMIN_EMAIL" --password "$E2E_ADMIN_PASSWORD" --name "E2E Admin" --admin >/tmp/ensure-user.log 2>&1
setsid nohup dotnet run --project src/Channeled.Api -c Release --no-build --launch-profile http >/tmp/channeled-e2e-api.log 2>&1 < /dev/null &
for i in $(seq 1 90); do
  curl -fsS http://127.0.0.1:5048/api/v1/health >/dev/null 2>&1 && break
  sleep 1
done
curl -fsS http://127.0.0.1:5048/api/v1/health >/dev/null
cd web
if [[ "${SKIP_WEB_BUILD:-0}" != "1" ]]; then
  npm run build >/tmp/web-build.log 2>&1
fi
setsid nohup npx vite preview --host 127.0.0.1 --port 5173 >/tmp/channeled-e2e-web.log 2>&1 < /dev/null &
for i in $(seq 1 30); do
  curl -fsS http://127.0.0.1:5173 >/dev/null 2>&1 && break
  sleep 1
done
curl -fsS http://127.0.0.1:5173 >/dev/null
echo "servers up"
