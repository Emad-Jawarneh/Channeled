#!/usr/bin/env bash
set -euo pipefail
cd /home/jawarneh/Channeled
export PGPASSWORD=channeled_dev

echo "=== migrations applied ==="
psql -h 127.0.0.1 -U channeled -d channeled -tAc 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1;'

echo "=== AspNetUsers profile cols ==="
psql -h 127.0.0.1 -U channeled -d channeled -tAc \
  "SELECT column_name FROM information_schema.columns WHERE table_name='AspNetUsers' AND (column_name ILIKE '%Bio%' OR column_name ILIKE '%Avatar%' OR column_name ILIKE '%Slug%' OR column_name ILIKE '%Username%') ORDER BY 1;"

echo "=== teams join/logo cols ==="
psql -h 127.0.0.1 -U channeled -d channeled -tAc \
  "SELECT column_name FROM information_schema.columns WHERE table_name='teams' AND (column_name ILIKE '%Logo%' OR column_name ILIKE '%Join%' OR column_name ILIKE '%Default%') ORDER BY 1;"

echo "=== tables account-related ==="
psql -h 127.0.0.1 -U channeled -d channeled -tAc \
  "SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename IN ('username_reservations','pending_email_changes','operations_settings','team_join_requests') ORDER BY 1;"

echo "=== API process ==="
ss -tlnp 2>/dev/null | grep 5048 || true
ps -p 3424700 -o pid,lstart,cmd 2>/dev/null || true

echo "=== unauthenticated probes ==="
curl -s -o /tmp/csrf.json -w "csrf:%{http_code}\n" http://127.0.0.1:5048/api/v1/auth/csrf
curl -s -o /tmp/teams.json -w "teams:%{http_code}\n" http://127.0.0.1:5048/api/v1/teams/
curl -s -o /tmp/me.json -w "me:%{http_code}\n" http://127.0.0.1:5048/api/v1/auth/me
echo -n "teams body: "; head -c 400 /tmp/teams.json; echo
echo -n "me body: "; head -c 400 /tmp/me.json; echo
