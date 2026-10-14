#!/usr/bin/env bash
set -euo pipefail
cd /home/jawarneh/Channeled

# 1) Park all WIP (tracked + untracked) on a dedicated branch commit
git checkout -B wip/account-profiles
git add -A
git status --short | head -5
git commit -m "$(cat <<'EOF'
WIP: account profiles, avatars, self-service mail, bio, uploaded-by column

Preserve in-progress Account/Profiles work before merging Roles-and-visibility-redesign.
EOF
)"

# 2) Fast-forward main to include the feature branch
git checkout main
git merge --ff-only Roles-and-visibility-redesign

# 3) Merge WIP account work onto main
git merge --no-edit wip/account-profiles

echo "=== RESULT ==="
git status -sb
git log --oneline -12
