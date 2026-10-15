#!/usr/bin/env bash
# Start API + Vite, seed e2e admin, run Playwright.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

export NVM_DIR="${NVM_DIR:-$HOME/.nvm}"
# shellcheck disable=SC1090
[[ -s "$NVM_DIR/nvm.sh" ]] && . "$NVM_DIR/nvm.sh"

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ConnectionStrings__Postgres="${ConnectionStrings__Postgres:-Host=127.0.0.1;Port=5432;Database=channeled;Username=channeled;Password=channeled_dev}"
export FileStore__Kind=FileSystem
export FileStore__RootPath="${FileStore__RootPath:-$ROOT/.data/e2e-object-store}"
export Backup__RootPath="${Backup__RootPath:-$ROOT/.data/e2e-backups}"
export DisableBackgroundJobs=true
export MigrateOnStartup=true
# Specs seed many users and messages through the API from one IP (mirrors AuthApiFactory).
export RateLimits__AuthPermitPerMinute="${RateLimits__AuthPermitPerMinute:-100000}"
export Chat__PostsPerMinutePerUser="${Chat__PostsPerMinutePerUser:-100000}"
# 2 MiB/s per download stream so downloads.spec.ts can prove the browser starts before the body arrives.
export Downloads__PerStreamBytesPerSecond="${Downloads__PerStreamBytesPerSecond:-2097152}"
export E2E_ADMIN_EMAIL="${E2E_ADMIN_EMAIL:-e2e-admin@example.com}"
export E2E_ADMIN_PASSWORD="${E2E_ADMIN_PASSWORD:-E2eAdminPassword1!}"
# Unset skip so host validation runs when deps are installed; allow override.
export PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS="${PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS:-0}"

mkdir -p "$FileStore__RootPath" "$Backup__RootPath"

dotnet build src/Channeled.Api -c Release --nologo -v q

# Ensure e2e platform admin exists (works even if another operator was seeded).
dotnet run --project src/Channeled.Api -c Release --no-build -- ensure-user \
  --email "$E2E_ADMIN_EMAIL" \
  --password "$E2E_ADMIN_PASSWORD" \
  --name "E2E Admin" \
  --admin

dotnet run --project src/Channeled.Api -c Release --no-build --launch-profile http >/tmp/channeled-e2e-api.log 2>&1 &
API_PID=$!
cleanup() {
  kill "$API_PID" "$WEB_PID" 2>/dev/null || true
}
trap cleanup EXIT

# Wait for API
for i in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:5048/api/v1/health" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
curl -fsS "http://127.0.0.1:5048/api/v1/health" >/dev/null

pushd web >/dev/null
npm ci --prefer-offline
npx playwright install chromium firefox
npm run build
npx vite preview --host 127.0.0.1 --port 5173 >/tmp/channeled-e2e-web.log 2>&1 &
WEB_PID=$!
popd >/dev/null

for i in $(seq 1 30); do
  if curl -fsS "http://127.0.0.1:5173" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

export PLAYWRIGHT_BASE_URL=http://127.0.0.1:5173
# Proxy API through vite preview — configure preview proxy
pushd web >/dev/null
npx playwright test "$@"
popd >/dev/null

echo "=== e2e green ==="
