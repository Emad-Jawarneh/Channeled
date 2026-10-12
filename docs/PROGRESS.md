# Channeled Progress

## Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0–M7 | ✅ Complete | Prior gates |
| M8 Hardening | ✅ Complete | Playwright e2e + axe; `docs/SECURITY.md`; load-test script; `./ci.sh` green |

## M8 exit gate

- [x] Playwright e2e: sign in; upload; rename; move; delete; restore; invite/accept; role change; private channel; personal copy; admin audit
- [x] Accessibility (axe) pass on channel browser
- [x] `docs/SECURITY.md` ASVS L2 filled
- [x] Load test script (CI reduced profile; full 200/50/1M documented)
- [x] README quickstart; `./ci.sh` green

## M8 notes

- SPA: rail/sidebar/main/details; routes for login, invite, reset, channel files, personal, search, team settings, admin
- Upload tray with multipart concurrency; list refresh on upload complete
- `scripts/e2e.sh`, `scripts/load-test.mjs`; `ensure-user` CLI for e2e admin
- Playwright OS deps: `sudo npx playwright install-deps` (once per machine)
