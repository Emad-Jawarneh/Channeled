# 09 — Admin, ops & configuration

## Platform admin API

Prefix: `/api/v1/admin/*` — requires `IsPlatformAdmin` and active status; others get **404**.

| Area | Capabilities |
|------|----------------|
| Dashboard / health | Counts, basic health |
| Users | List/create/update, disable/enable, delete-preview/delete, reset password |
| Teams | List, patch quota |
| Audit | Filterable list + export |
| Backups | Run, list, record restore |
| Store | Reconcile report |
| Ops | SMTP test, admin email correction, config-change audit stub |
| Files | Download-audit hook, hard purge |

Audit catalog of emit-able event types: `src/Channeled.Domain/Audit/AuditCatalog.cs`. Table is append-only for the app role (DB privileges / triggers as deployed).

SPA: `web/src/features/admin/*` (dashboard, users with delete dialog, teams, audit, health, storage).

## Operator CLI (`Program.cs`)

Early-exit modes before `app.Run`:

| Command | Purpose |
|---------|---------|
| `seed-operator` | First platform admin (once) |
| `ensure-user` | Create/update user; optional `--admin` |
| `delete-user` | Same CAS delete path as admin API (password + resolutions) |
| `backup` / `restore` / `restore-drill` | Backup pipeline |
| `reconcile-store` | Store vs DB report |
| `seed-bench-files` | Search/load bench data |

See `docs/OPERATOR_GUIDE.md` for env vars and examples.

## Ops tree

```text
ops/
├── postgresql/install.sh
├── minio/install.sh
├── backup/{backup,restore,restore-drill}.sh
├── nginx/ · caddy/          # TLS / headers samples
├── channeled-api.service   # systemd
└── channeled.env.example   # secrets template
```

Native install only — no Docker Compose path.

## Configuration reference

Defaults: `src/Channeled.Api/appsettings.json`. Production: environment variables / `channeled.env` (see example).

| Section | Salient keys |
|---------|--------------|
| `ConnectionStrings:Postgres` | Prefer `DATABASE_URL` when set; localhost default only in Development appsettings |
| `MigrateOnStartup` | Apply EF migrations on boot |
| `DisableBackgroundJobs` | Kill switch for hosted services |
| `FileStore` | `Kind`, filesystem root **or** S3 endpoint/bucket/keys |
| `UploadLimits` | Max file, part size, session expiry, **TrashRetentionDays** |
| `Downloads` | Ticket windows, concurrent stream caps |
| `PublicFileLinks` | Enabled, HMAC keys, expiry, anon egress caps |
| `Chat` | Posts/min, deleted retention, purge interval |
| `ChannelDelete` | Per-txn / per-team caps, trash item cap, object_delete chunk/attempts, lock/statement timeouts |
| `UserDelete` | Sweep interval, late-phase grace |
| `TeamJoin` | Max pending when converting to open join |
| `Account` | Self-service mail mode, session cache TTL ≤5s, rate limits, email-change / username cooldowns |
| `Realtime` | AllowedOrigins, connection/group caps, coalesce timings |
| `Backup` | Root, retain counts, pg_dump paths, low disk, RestoreViaAdmin |
| `Maintenance` | Global 503 mode |
| `Operations` | PublicUrl, SMTP* |
| `SearchBench` | P95 gate, seed counts |
| `RateLimits` | Auth permits per minute |

### Deletion-related knobs (quick)

```json
"ChannelDelete": {
  "MaxFilesPerTxn": 10000,
  "MaxMessagesPerTxn": 10000,
  "MaxFilesPerTeamDelete": 50000,
  "MaxMessagesPerTeamDelete": 50000,
  "MaxItemsPerTrash": 10000,
  "ObjectDeleteChunkSize": 2000,
  "LockTimeout": "5s",
  "StatementTimeout": "60s"
},
"UserDelete": {
  "SweepIntervalSeconds": 60,
  "LatePhaseGraceSeconds": 30
},
"UploadLimits": {
  "TrashRetentionDays": 30
}
```

## Health & metrics

- `/health` and `/api/v1/health` — liveness/readiness style checks for proxies.
- `/metrics` — process metrics scrape endpoint.
- Maintenance mode still allows health/metrics (and selected admin backup routes).

## Security ops checklist

Cross-read `docs/SECURITY.md` (ASVS L2). Production expectations:

- HTTPS at reverse proxy; HSTS/CSP samples in `ops/`.
- Secrets only via env; rotate `PublicFileLinks` HMAC separately from DB backups.
- Closed-network pilots may defer 2FA; keep lockout + session revoke enabled.
