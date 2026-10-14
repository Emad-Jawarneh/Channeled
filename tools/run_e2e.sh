#!/usr/bin/env bash
set -euo pipefail
cd /home/jawarneh/Channeled

echo "=== free ports 5048 / 5173 ==="
# Kill listeners on those ports without nuking this shell.
for port in 5048 5173; do
  pids=$(ss -tlnp 2>/dev/null | awk -v p=":$port" '$4 ~ p {print}' | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | sort -u)
  if [[ -n "${pids:-}" ]]; then
    echo "killing pids on $port: $pids"
    # shellcheck disable=SC2086
    kill $pids 2>/dev/null || true
  fi
done
sleep 2
ss -tlnp 2>/dev/null | grep -E '5048|5173' || echo "ports free"

chmod +x scripts/e2e.sh
exec ./scripts/e2e.sh
