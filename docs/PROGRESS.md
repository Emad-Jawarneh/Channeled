# Channeled Progress

## Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0–M6 | ✅ Complete | Prior gates |
| M7 Operations | ✅ Complete | `RestoreDrillTests`; `./ci.sh` green |
| M8 Hardening | ⏸ | — |

## M7 exit gate

- [x] Backups: `pg_dump` + object copy + manifest + post-backup verification
- [x] Guided restore with maintenance mode; store↔DB reconciliation
- [x] Scripted restore drill reproduces data with **zero** reconcile diffs
- [x] Health (API/DB/store/disk/jobs), `/metrics`, correlation IDs, operator alerts
- [x] Install scripts + operator guide (`ops/`, `docs/OPERATOR_GUIDE.md`)
- [x] `./ci.sh` green

## M7 notes

- `IBackupService` / `BackupEntity`; CLI: `backup`, `restore`, `reconcile-store`, `restore-drill`
- Scripts: `ops/backup/{backup,restore,restore-drill}.sh`; Caddy/nginx samples; Windows service stub
- Admin `POST /backups/run` creates real packages; restore via admin controlled by `Backup:RestoreViaAdmin`
- Daily `BackupHostedService` when background jobs enabled
