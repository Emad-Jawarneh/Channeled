#!/usr/bin/env bash
set -euo pipefail
API=http://127.0.0.1:5048
COOKIE=/tmp/channeled-smoke.jar
rm -f "$COOKIE"

login() {
  local email="$1" pass="$2"
  curl -s -c "$COOKIE" -b "$COOKIE" -o /tmp/login.json -w "%{http_code}" \
    -H 'Content-Type: application/json' \
    -d "{\"email\":\"$email\",\"password\":\"$pass\"}" \
    "$API/api/v1/auth/login"
}

echo "=== try logins ==="
for pair in \
  'admin@example.com|AdminPassword1!' \
  'admin@example.com|AdminPassword3!' \
  'owner@example.com|OwnerPassword1!' \
  'emad@example.com|Password1!' \
  'test@example.com|Password1!'; do
  email="${pair%%|*}"
  pass="${pair##*|}"
  code=$(login "$email" "$pass" || true)
  echo "login $email -> $code $(head -c 120 /tmp/login.json 2>/dev/null)"
  if [[ "$code" == "200" ]]; then
    OK_EMAIL=$email
    break
  fi
done

if [[ -z "${OK_EMAIL:-}" ]]; then
  echo "No known login worked; listing users:"
  export PGPASSWORD=channeled_dev
  psql -h 127.0.0.1 -U channeled -d channeled -c 'SELECT "Email","UserName","UsernameSlug","Status" FROM "AspNetUsers" LIMIT 20;'
  exit 1
fi

# CSRF
csrf=$(curl -s -c "$COOKIE" -b "$COOKIE" "$API/api/v1/auth/csrf" | python3 -c 'import sys,json; print(json.load(sys.stdin)["token"])')
HDR=(-H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json')

probe() {
  local method="$1" path="$2"
  local code
  code=$(curl -s -c "$COOKIE" -b "$COOKIE" "${HDR[@]}" -X "$method" -o /tmp/probe.json -w "%{http_code}" "$API$path")
  echo "$method $path -> $code $(head -c 180 /tmp/probe.json | tr '\n' ' ')"
}

echo "=== authenticated probes as $OK_EMAIL ==="
probe GET /api/v1/auth/me
probe GET /api/v1/teams/
probe GET /api/v1/me/files/items
probe GET /api/v1/account/self-service-mail
probe GET /api/v1/search?q=test

# If teams exist, probe first team
slug=$(python3 -c 'import json; d=json.load(open("/tmp/probe.json")); print(d[0]["slug"] if isinstance(d,list) and d else "")' 2>/dev/null || true)
# re-get teams
curl -s -c "$COOKIE" -b "$COOKIE" "${HDR[@]}" -o /tmp/teams.json "$API/api/v1/teams/"
slug=$(python3 -c 'import json; d=json.load(open("/tmp/teams.json")); print(d[0]["slug"] if isinstance(d,list) and d else "")')
echo "first team slug: ${slug:-none}"
if [[ -n "$slug" ]]; then
  probe GET "/api/v1/teams/$slug"
  probe GET "/api/v1/teams/$slug/channels"
  probe GET "/api/v1/teams/$slug/members"
  probe GET "/api/v1/teams/$slug/channels/general/items"
  probe GET "/api/v1/teams/$slug/logo"
fi

echo "=== recent errors in api log ==="
grep -E 'fail:|Error|Exception|500|42P01|42703' /tmp/channeled-api.log | tail -40 || true
