#!/usr/bin/env bash
# Dev helper: run SQL from a file against the test database. Usage: sql.sh <file.sql> [db]
PGPASSWORD=channeled_dev psql -h 127.0.0.1 -U channeled -d "${2:-channeled_test}" -P pager=off -f "$1"
