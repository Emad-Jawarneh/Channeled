#!/usr/bin/env bash
set -euo pipefail
cd /home/jawarneh/Channeled

echo "=== kill old API on 5048 ==="
if pid=$(ss -tlnp 2>/dev/null | sed -n 's/.*pid=\([0-9]*\).*5048.*/\1/p' | head -1); then
  echo "pid from ss parse may be wrong; finding by port"
fi
pids=$(ss -tlnp 2>/dev/null | awk '/5048/ {print}' || true)
echo "$pids"
# Kill Channeled.Api listening on 5048
pkill -f 'Channeled.Api --urls http://127.0.0.1:5048' 2>/dev/null || true
pkill -f 'Channeled.Api.dll' 2>/dev/null || true
sleep 1
ss -tlnp 2>/dev/null | grep 5048 || echo "port 5048 free"

echo "=== build ==="
dotnet build src/Channeled.Api/Channeled.Api.csproj -c Debug -v q

echo "=== apply migrations ==="
dotnet ef database update --project src/Channeled.Infrastructure --startup-project src/Channeled.Api

echo "=== verify TeamLogo applied ==="
export PGPASSWORD=channeled_dev
psql -h 127.0.0.1 -U channeled -d channeled -tAc \
  'SELECT "MigrationId" FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '\''%TeamLogo%'\'';'
psql -h 127.0.0.1 -U channeled -d channeled -tAc \
  "SELECT column_name FROM information_schema.columns WHERE table_name='teams' AND column_name ILIKE '%Logo%' ORDER BY 1;"

echo "=== start API ==="
nohup dotnet run --no-build -c Debug --project src/Channeled.Api --urls http://127.0.0.1:5048 \
  > /tmp/channeled-api.log 2>&1 &
echo "started pid $!"
sleep 4
curl -s -o /dev/null -w "csrf:%{http_code}\n" http://127.0.0.1:5048/api/v1/auth/csrf
tail -30 /tmp/channeled-api.log
