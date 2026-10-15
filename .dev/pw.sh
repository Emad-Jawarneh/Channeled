#!/usr/bin/env bash
# Dev helper. Usage: pw.sh <playwright args...> — runs against already-running e2e servers; log in /tmp/pw.log.
cd /home/jawarneh/Channeled/web
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5173 npx playwright test "$@" --reporter=line > /tmp/pw.raw.log 2>&1
code=$?
sed -r 's/\x1b\[[0-9;]*[A-Za-z]//g' /tmp/pw.raw.log > /tmp/pw.log
grep -E '^\s+[0-9]+ (passed|failed|skipped|flaky)|\[perf-probe\]|"(messages|rows|loadOlderP95Ms|loadMoreP95Ms|interactionP95Ms|selectP95Ms|checkP95Ms|processingP95Ms|presentationP95Ms|overBudget|maxMs|count)"|^\s+[0-9]+\) \[|^\s+Error: |^\s+Expected|^\s+Received|"rule"|"target"' /tmp/pw.log | head -${PW_LINES:-80}
exit $code
