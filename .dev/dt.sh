#!/usr/bin/env bash
# Dev helper. Usage: dt.sh <Api|Unit|Integration> [filter] [lines] — runs filtered tests, prints failures + summary.
# DT_DETAIL=1 adds assertion messages and stack traces (console logger, normal verbosity).
set -o pipefail
cd /home/jawarneh/Channeled
project="tests/Channeled.Tests.$1"
filter="${2:-}"
args=(test "$project" -c Release --nologo -v q)
if [[ -n "$filter" ]]; then args+=(--filter "$filter"); fi
if [[ "${DT_DETAIL:-0}" == "1" ]]; then args+=(--logger "console;verbosity=normal"); fi
dotnet "${args[@]}" > /tmp/dt.log 2>&1
code=$?
if [[ "${DT_DETAIL:-0}" == "1" ]]; then
  grep -vE '^\s*$|^\s+Passed |^Build|^  Determining|^  All projects' /tmp/dt.log | grep -E -A14 'Failed |Error Message' | head -${3:-120}
fi
grep -E 'error |Passed!|Failed!|\[FAIL\]' /tmp/dt.log | head -${3:-80}
exit $code
