#!/usr/bin/env bash
set -euo pipefail
cd /home/jawarneh/Channeled

pkill -f 'dotnet run .*Channeled.Api' 2>/dev/null || true
pkill -f 'Channeled.Api/bin/.*/Channeled.Api' 2>/dev/null || true
sleep 1

if pgrep -af 'Channeled.Api' | grep -v restart_api >/dev/null 2>&1; then
  echo "Still running:"
  pgrep -af 'Channeled.Api' || true
  pkill -9 -f 'Channeled.Api' 2>/dev/null || true
  sleep 1
fi

dotnet build src/Channeled.Api/Channeled.Api.csproj -c Debug -v q

nohup dotnet run --no-build -c Debug --project src/Channeled.Api --urls http://127.0.0.1:5048 \
  >/tmp/channeled-api.log 2>&1 &
echo "started pid $!"

for i in 1 2 3 4 5 6 7 8 9 10; do
  code=$(curl -s -m 2 -o /dev/null -w '%{http_code}' http://127.0.0.1:5048/api/v1/auth/csrf || true)
  if [[ "$code" == "200" ]]; then
    echo "API up (csrf=$code)"
    exit 0
  fi
  sleep 1
done

echo "API did not become ready; last log:"
tail -40 /tmp/channeled-api.log || true
exit 1
