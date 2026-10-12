#!/usr/bin/env bash
# Scripted restore drill: backup → wipe store → restore → reconcile (zero diffs).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
dotnet build src/Channeled.Api -c Release --nologo -v q
dotnet run --project src/Channeled.Api --no-build -c Release -- restore-drill
