#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export NVM_DIR="${NVM_DIR:-$HOME/.nvm}"
if [[ -s "$NVM_DIR/nvm.sh" ]]; then
  # shellcheck disable=SC1090
  . "$NVM_DIR/nvm.sh"
fi

if [[ ! -f openapi/v1.json ]]; then
  echo "Missing openapi/v1.json" >&2
  exit 1
fi

pushd web >/dev/null
if [[ ! -d node_modules ]]; then
  npm ci
fi
npm run generate:api
popd >/dev/null

echo "Generated web/src/api/generated.ts from openapi/v1.json"
