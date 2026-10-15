#!/usr/bin/env bash
# Dev helper: typecheck, lint errors only, and unit tests for web/.
cd /home/jawarneh/Channeled/web
echo "--- tsc ---"
npx tsc --noEmit --pretty false 2>&1 | head -30
echo "--- eslint errors ---"
npx eslint . 2>&1 | grep -B3 -E '^\s+[0-9]+:[0-9]+\s+error' | head -40
echo "--- vitest ---"
npx vitest run 2>&1 | grep -E 'FAIL|✗|×|Test Files|Tests ' | head -30
