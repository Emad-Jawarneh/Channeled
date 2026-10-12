# Channeled Progress

## Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0 Foundations | ✅ Complete | `./ci.sh` green |
| M1 Identity | ✅ Complete | `./ci.sh` exit 0 (2026-10-12). API tests: login, lockout, expired/reused invite+reset tokens, CSRF rejection. Seed operator works. |
| M2 Teams and channels | ⏸ Next | — |
| M3–M8 | ⏸ | — |

## M1 exit gate

- [x] Login / logout (cookie auth)
- [x] Sessions list / revoke
- [x] Platform invitations + accept
- [x] Password reset (hashed, single-use, expiring)
- [x] Lockout after 5 failures
- [x] Rate limits on auth endpoints
- [x] Audit foundation (same TX as change)
- [x] `seed-operator` command
- [x] API abuse tests (lockout, expired/reused tokens, CSRF)
- [x] Web login uses cookies (no localStorage auth)

## Verified

```text
./ci.sh
# Unit 9, Integration 2, Api 6 — all passed
# === CI green ===

dotnet run --project src/Channeled.Api -- seed-operator ...
# conflict: Operator already seeded.  (idempotent OK)
```

## Notes

- Default local DB password in `appsettings*.json` is for dev only; override via env in real deploys.
- Team-scoped invitations deferred to M2 (platform invitations cover M1 account creation).
