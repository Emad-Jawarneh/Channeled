#!/usr/bin/env bash
# Seed N file metadata rows for the M5 list/search bench (default 1_000_000).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

COUNT="${1:-1000000}"
EMAIL="${CHANNELED_BENCH_EMAIL:-bench@example.com}"
PASSWORD="${CHANNELED_BENCH_PASSWORD:-BenchPassword1!}"

dotnet run --project src/Channeled.Api --no-build -c Release -- \
  seed-bench-files --count "$COUNT" --email "$EMAIL" --password "$PASSWORD"
