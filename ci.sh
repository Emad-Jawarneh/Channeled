#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

# Prefer nvm Node if present (Ubuntu package often ships node without npm).
export NVM_DIR="${NVM_DIR:-$HOME/.nvm}"
if [[ -s "$NVM_DIR/nvm.sh" ]]; then
  # shellcheck disable=SC1090
  . "$NVM_DIR/nvm.sh"
fi

echo "=== Channeled CI (fail-closed) ==="

echo "[1/10] dotnet restore/build"
dotnet restore Channeled.slnx
dotnet build Channeled.slnx -c Release --no-restore

echo "[2/10] unit tests"
dotnet test tests/Channeled.Tests.Unit -c Release --no-build --verbosity minimal

echo "[3/10] integration tests"
dotnet test tests/Channeled.Tests.Integration -c Release --no-build --verbosity minimal

echo "[4/10] API tests"
dotnet test tests/Channeled.Tests.Api -c Release --no-build --verbosity minimal

echo "[5/10] web install/typecheck/lint/test/build"
if ! command -v npm >/dev/null 2>&1; then
  echo "npm is required for web CI steps" >&2
  exit 1
fi
pushd web >/dev/null
npm ci
npm run typecheck
npm run lint
npm run test -- --run
npm run build
node ../scripts/check-bundle-budget.mjs
popd >/dev/null

echo "[6/10] OpenAPI → TS client pipeline"
./scripts/generate-api-client.sh

echo "[7/10] dependency audits"
audit_out="$(dotnet list src/Channeled.Api package --vulnerable --include-transitive 2>&1)"
printf '%s\n' "$audit_out"
if ! printf '%s\n' "$audit_out" | grep -Fq 'has no vulnerable packages'; then
  echo "Vulnerable .NET packages detected" >&2
  exit 1
fi
pushd web >/dev/null
npm audit --audit-level=high
popd >/dev/null

echo "[8/10] format check"
dotnet format Channeled.slnx --verify-no-changes --severity warn

echo "[9/10] Playwright e2e + axe"
chmod +x scripts/e2e.sh
./scripts/e2e.sh

echo "[10/10] load test (reduced CI profile)"
dotnet run --project src/Channeled.Api -c Release --no-build --launch-profile http >/tmp/channeled-load-api.log 2>&1 &
LOAD_PID=$!
cleanup_load() { kill "$LOAD_PID" 2>/dev/null || true; }
trap cleanup_load EXIT
for i in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:5048/api/v1/health" >/dev/null 2>&1; then break; fi
  sleep 1
done
export LOAD_EMAIL="${E2E_ADMIN_EMAIL:-e2e-admin@example.com}"
export LOAD_PASSWORD="${E2E_ADMIN_PASSWORD:-E2eAdminPassword1!}"
LOAD_USERS=5 LOAD_ITERATIONS=10 LOAD_P95_MAX_MS=5000 node scripts/load-test.mjs
cleanup_load
trap - EXIT

echo "=== CI green ==="
