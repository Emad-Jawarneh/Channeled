#!/usr/bin/env bash
set -euo pipefail
cd /home/jawarneh/Channeled

echo "=== HEAD ==="
git rev-parse --abbrev-ref HEAD
git rev-parse --short HEAD

echo
echo "=== Commits on Roles-and-visibility-redesign NOT on main ==="
git log --oneline main..Roles-and-visibility-redesign

echo
echo "=== Commits on main NOT on Roles-and-visibility-redesign ==="
git log --oneline Roles-and-visibility-redesign..main

echo
echo "=== Local feature commits not on origin/feature (if remote exists) ==="
if git rev-parse --verify origin/Roles-and-visibility-redesign >/dev/null 2>&1; then
  git log --oneline origin/Roles-and-visibility-redesign..Roles-and-visibility-redesign
else
  echo "(no origin/Roles-and-visibility-redesign)"
fi

echo
echo "=== Working tree (count) ==="
git status --short | wc -l
echo "modified tracked:"
git status --short | grep -c '^ M\|^M ' || true
echo "untracked paths:"
git status --short | grep -c '^??' || true
