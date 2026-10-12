#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ID="${1:?backup id required}"
cd "$ROOT"
dotnet run --project src/Channeled.Api --no-build -c Release -- restore --id "$ID"
