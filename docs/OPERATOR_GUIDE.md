# Operator Guide

Native install only — **no Docker**.

## Prerequisites

- .NET 10 runtime (SDK for builds)
- PostgreSQL 16+ (`pg_dump` / `pg_restore` on `PATH`)
- Optional: MinIO native binary, Caddy or nginx, Node 20+ (SPA build)

## Configuration

One settings file plus environment overrides. Start from [`ops/channeled.env.example`](../ops/channeled.env.example).

| Setting | Purpose |
|---------|---------|
| `ConnectionStrings__Postgres` | Database |
| `FileStore__Kind` / `RootPath` or S3 fields | Object store |
| `Backup__RootPath` | Backup packages |
| `Backup__RetainDaily` / `Weekly` / `Monthly` | Retention (7 / 4 / 3) |
| `Backup__AlertEmail` | Operator alerts |
| `MigrateOnStartup` | Apply EF migrations on boot |
| `Maintenance__Enabled` | Global read-only maintenance (503) |

## Linux (systemd)

```bash
./ops/postgresql/install.sh
./ops/minio/install.sh   # optional
sudo mkdir -p /opt/channeled /etc/channeled /var/lib/channeled/{object-store,backups}
dotnet publish src/Channeled.Api -c Release -o /opt/channeled
sudo cp ops/channeled.env.example /etc/channeled/channeled.env   # edit secrets
sudo cp ops/channeled-api.service /etc/systemd/system/
sudo systemctl daemon-reload && sudo systemctl enable --now channeled-api
```

TLS: use [`ops/caddy/Caddyfile`](../ops/caddy/Caddyfile) or [`ops/nginx/channeled.conf`](../ops/nginx/channeled.conf).

## Windows

```powershell
dotnet publish src/Channeled.Api -c Release -o C:\channeled
# edit env / appsettings
ops\windows\install-api-service.ps1 -InstallDir C:\channeled
```

## Health and metrics

```bash
curl -s http://127.0.0.1:5000/health
curl -s http://127.0.0.1:5000/metrics
```

Health covers API process, database, object store, disk free space, and pending jobs. Responses include `X-Correlation-ID`.

## Backups

On-demand (also available as platform admin `POST /api/v1/admin/backups/run`):

```bash
dotnet build src/Channeled.Api -c Release
./ops/backup/backup.sh ondemand
```

Daily backups also run from the hosted job when `DisableBackgroundJobs` is false.

Each package under `Backup__RootPath/<id>/` contains:

- `database.dump` — `pg_dump -Fc`
- `objects/` — file bytes
- `manifest.json` — keys, sizes, SHA-256

Post-backup verification checks dump presence and per-object checksums.

### Restore (maintenance mode)

```bash
./ops/backup/restore.sh <backup-guid>
```

Restore enables maintenance mode, terminates other DB sessions, `pg_restore --clean`, restores objects from the manifest, then runs store↔DB reconciliation (must be zero diffs).

### Restore drill (M7 gate)

```bash
./ops/backup/restore-drill.sh
# or: dotnet test tests/Channeled.Tests.Integration --filter RestoreDrill
```

## Store reconciliation

```bash
dotnet run --project src/Channeled.Api -- reconcile-store
# or admin: POST /api/v1/admin/jobs/store-reconcile
```

Reports missing objects, orphans, and checksum mismatches.

## Alerts

Configure `Backup__AlertEmail`. Failures (backup errors, low disk below `Backup__LowDiskBytes`) are emailed via the configured `IEmailSender` (logs in development).

## Seed first operator

```bash
dotnet run --project src/Channeled.Api -- seed-operator \
  --email you@example.com --password 'YourPassword1!' --name Operator
```

## CI

```bash
./ci.sh
```
