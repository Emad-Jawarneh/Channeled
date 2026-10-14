#!/usr/bin/env bash
set -euo pipefail
cd /home/jawarneh/Channeled

git add -A
git status --short | head -20

# Ensure no conflict markers remain
if git grep -n '<<<<<<<' -- ':!tools' >/dev/null 2>&1; then
  echo "CONFLICT MARKERS REMAIN"
  git grep -n '<<<<<<<' -- ':!tools' || true
  exit 1
fi

dotnet build src/Channeled.Api/Channeled.Api.csproj -c Debug -v q
cd web && npm run build 2>&1 | tail -25
cd ..

git commit -m "$(cat <<'EOF'
Merge account profiles WIP onto Roles-and-visibility-redesign

Keep team privacy/join/logo work and account/avatars/SMTP/bio/uploaded-by without dropping either side.
EOF
)"

git status -sb
git log --oneline -10
echo "DONE"
