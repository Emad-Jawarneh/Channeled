#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
KIND="${1:-ondemand}"
cd "$ROOT"
dotnet run --project src/Channeled.Api --no-build -c Release -- backup --kind "$KIND"
