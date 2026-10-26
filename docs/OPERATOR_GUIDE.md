# Operator Guide

**Native install** (systemd / Windows service) is the primary production path. An optional **Docker Compose** stack is documented in [`docker/README.md`](docker/README.md).

For as-built architecture (deletion lifecycles, jobs, config map), see [`system-design/`](system-design/README.md).

## Prerequisites

- .NET 10 runtime (SDK for builds) — or Docker Engine 24+ / Compose v2 for the container path
- PostgreSQL **15+** required (**16+** recommended); `pg_dump` / `pg_restore` on `PATH`. Share-link FKs use `ON DELETE SET NULL (file_id)` (PG 15+).
- Optional: MinIO native binary, Caddy or nginx, Node 20+ (SPA build)

## Docker (optional)

```bash
cp ops/docker/compose.env.example ops/docker/compose.env   # set POSTGRES_PASSWORD
docker compose --env-file ops/docker/compose.env up -d --build
docker compose --env-file ops/docker/compose.env exec api \
  dotnet Channeled.Api.dll seed-operator \
  --email you@example.com --password 'YourPassword1!' --name Operator
```

Details, MinIO profile, volumes, and TLS notes: [`docker/README.md`](docker/README.md).

## Configuration

One settings file plus environment overrides. Start from [`ops/channeled.env.example`](../ops/channeled.env.example).

| Setting | Purpose |
|---------|---------|
| `ConnectionStrings__Postgres` / `DATABASE_URL` | Database (`DATABASE_URL` wins; Railway: link Postgres so the plugin injects it) |
| `FileStore__Kind` / `RootPath` or S3 fields | Object store |
| `Backup__RootPath` | Backup packages |
| `Backup__RetainDaily` / `Weekly` / `Monthly` | Retention (7 / 4 / 3) |
| `Backup__AlertEmail` | Operator alerts |
| `MigrateOnStartup` | Apply EF migrations on boot |
| `Maintenance__Enabled` | Global read-only maintenance (503) |
| `Operations__PublicUrl` | Public origin used in share URLs |
| `Realtime__AllowedOrigins` | Hub `Origin` allow-list (SPA origins) |
| `PublicFileLinks__Enabled` | Kill-switch for anonymous file links (default **false**; `IOptionsMonitor`) |
| `PublicFileLinks__HmacKey` / `PreviousHmacKey` | Ticket HMAC secrets (**not** in DB backups — store/restore separately) |
| `Chat__PostsPerMinutePerUser` | Text-channel post rate limit (default 30) |
| `ChannelDelete__MaxFilesPerTeamDelete` / `MaxMessagesPerTeamDelete` | Team delete totals (default 50000) |
| `UserDelete__SweepIntervalSeconds` / `LatePhaseGraceSeconds` | Stuck user-delete sweep |

### Railway

Use Railpack (`railway.toml` → `builder = "RAILPACK"`), not the root Dockerfile. Link a Postgres plugin to the service so Railway injects `DATABASE_URL`. Localhost Postgres defaults exist only under `ASPNETCORE_ENVIRONMENT=Development`. Also set `Operations__PublicUrl`, `Realtime__AllowedOrigins__0`, and Data Protection / file-store volume paths (see `railpack.json` deploy variables).

### Account deletion and chat privacy

Deleting a user **keeps** their team file uploads and text-channel message bodies. The UI shows **Deleted user** for authors/uploaders. This is intentional. Removing specific messages or files after the fact requires a **separate admin scrub tool** (not shipped yet). Personal files and avatars are discarded as part of account delete.

CLI (same checks as the admin API; transfers sole-owned teams to the first eligible member unless you resolve over-cap teams in the UI first):

```bash
dotnet run --project src/Channeled.Api -- delete-user \
  --actor-email admin@example.com --actor-password '…' \
  --email user@example.com
```

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

### Reverse proxy (WebSockets + forwarded headers)

- Proxy `/hubs/` with WebSocket upgrade (Caddy/`nginx` samples must pass `Upgrade`/`Connection`).
- Configure trusted proxies so `ForwardedHeaders` yields real client IPs (anon share rate limits and stream caps key on IP).
- SPA and API same origin in production; add the public SPA origin to `Realtime__AllowedOrigins`.

### Public file links

1. Set a long random `PublicFileLinks__HmacKey` (and rotate via `PreviousHmacKey` during ticket lifetime).
2. Set `PublicFileLinks__Enabled=true` only when ready.
3. Backup the HMAC key **separately** from PostgreSQL dumps if outstanding tickets must survive machine migration; a restore without the key invalidates tickets (re-claim required). DB restore also revokes all link rows.

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

## SMTP and self-service mail

- Set `Operations__SmtpHost`, `Operations__SmtpPort`, `Operations__SmtpFrom`, and optional username/password. When `SmtpHost` is set, the API uses `SmtpEmailSender`; otherwise it uses the logging sender (bodies redacted).
- `Account__SelfServiceMail` = `Auto` (default) or `Off`. Self-service password reset and email change are available only when the **SMTP sender is active** and an admin has run a successful SMTP test (`smtp_verified_at`). Changing SMTP settings clears verification.
- Development: run [Mailpit](https://github.com/axllent/mailpit) (or similar) as the SMTP target so reset/confirm links are visible. Logging sender alone redacts tokens.
- Email send jobs encrypt payloads with Data Protection. After restore without DP keys, failed decrypts go to **dead** with an admin-visible reason (not infinite retry). Preserve the data-protection key ring across restores.
- Confirm/reset links use `#token=` fragments; if a mail client drops the fragment, users can paste the token shown in the email body.

## Seed first operator

```bash
dotnet run --project src/Channeled.Api -- seed-operator \
  --email you@example.com --password 'YourPassword1!' --name Operator
```

## CI

```bash
./ci.sh
```
