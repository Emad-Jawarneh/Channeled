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

echo "[1/8] dotnet restore/build"
dotnet restore Channeled.slnx
dotnet build Channeled.slnx -c Release --no-restore

echo "[2/8] unit tests"
dotnet test tests/Channeled.Tests.Unit -c Release --no-build --verbosity minimal

echo "[3/8] integration tests"
dotnet test tests/Channeled.Tests.Integration -c Release --no-build --verbosity minimal

echo "[4/8] API tests"
dotnet test tests/Channeled.Tests.Api -c Release --no-build --verbosity minimal

echo "[5/8] web install/typecheck/lint/test/build"
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
popd >/dev/null

echo "[6/8] OpenAPI → TS client pipeline"
./scripts/generate-api-client.sh

echo "[7/8] dependency audits"
audit_out="$(dotnet list src/Channeled.Api package --vulnerable --include-transitive 2>&1)"
printf '%s\n' "$audit_out"
if ! printf '%s\n' "$audit_out" | grep -Fq 'has no vulnerable packages'; then
  echo "Vulnerable .NET packages detected" >&2
  exit 1
fi
pushd web >/dev/null
npm audit --audit-level=high
popd >/dev/null

echo "[8/8] format check"
dotnet format Channeled.slnx --verify-no-changes --severity warn

echo "=== CI green ==="
