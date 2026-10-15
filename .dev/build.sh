#!/usr/bin/env bash
# Dev helper: build the whole solution and print only errors/warnings (deduplicated).
cd /home/jawarneh/Channeled
dotnet build Channeled.slnx -c Release --nologo -v q > /tmp/build.log 2>&1
code=$?
grep -E 'error |warning CS|warning CA' /tmp/build.log | sed -E 's/\[[^]]*\]$//' | sort -u | head -${1:-40}
echo "build exit: $code"
exit $code
