#!/usr/bin/env bash
set -euo pipefail
export PGPASSWORD=channeled_dev
psql -h 127.0.0.1 -U channeled -d channeled -c '\d "AspNetUsers"'
echo '---'
psql -h 127.0.0.1 -U channeled -d channeled -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 15;'
