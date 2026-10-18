# 08 — Jobs & background work

## Design principles

1. **Commit metadata first**, then remove bytes (or enqueue byte deletion). Never leave store keys without a recoverable DB/outbox trail that reconcile can explain.
2. Prefer **DB-backed jobs** with `FOR UPDATE SKIP LOCKED` claiming for multi-worker safety (single process today; pattern still applies).
3. Hosted services are gated by `DisableBackgroundJobs` (tests often disable or lengthen intervals).

## Job table

`jobs` / `JobEntity`:

| Field | Role |
|-------|------|
| `Type` | e.g. `object_delete`, `email_send` |
| `State` | `pending` → `running` → `completed` \| `dead` |
| `DetailsJson` | Payload (keys, multipart ids, mail fields) |
| `Attempts` / `AvailableAt` | Backoff scheduling |
| `Error` | Last failure |

### object_delete

- Enqueued via `ObjectDeleteOutbox.Enqueue` (chunked by `ChannelDelete:ObjectDeleteChunkSize`).
- Processor: `ObjectDeleteJobProcessor` — abort multiparts, delete keys from `IFileStore`.
- Max attempts: `ChannelDelete:ObjectDeleteMaxAttempts` (default 8) with exponential backoff.
- Hosted: `ObjectDeleteHostedService`.

Used by: trash purge, team/channel discard, personal discard, avatar/logo removal, admin file purge path (may also delete synchronously after row commit).

### email_send

- Queued mail when SMTP is configured.
- Processor: `EmailSendJobProcessor`; hosted `EmailSendHostedService`.

## Hosted services (Infrastructure unless noted)

| Service | Purpose | Key config |
|---------|---------|------------|
| `TrashPurgeHostedService` | Expire `trash_batches` past retention | `UploadLimits:TrashRetentionDays` |
| `ObjectDeleteHostedService` | Drain object_delete jobs | ChannelDelete chunk/attempts |
| `EmailSendHostedService` | Drain email jobs | Operations SMTP |
| `QuotaReconciliationHostedService` | Recompute usage counters | — |
| `BackupHostedService` | Scheduled backups (if enabled) | `Backup:*` |
| `ChatPurgeHostedService` | Hard-remove old soft-deleted messages | `Chat:DeletedRetentionDays` |
| `UserDeleteSweepHostedService` | Finish stuck late-phase user deletes | `UserDelete:SweepIntervalSeconds`, `LatePhaseGraceSeconds` |
| `UsernameReservationSweepHostedService` | Username cooldown cleanup | Account username rules |
| `AuthStateChangedService` (Api) | Push auth-state invalidation | `Realtime:AuthStateSweepSeconds` |

## Trash purge algorithm

`TrashPurgeService.PurgeBatchAsync`:

1. Load batch; mark `State = purging` (same txn).
2. Collect file ids/keys; delete stars + file rows.
3. Delete folders leaf-first (children before parents) within the batch.
4. Decrement team/personal `UsageBytes` with `GREATEST(…, 0)`.
5. Enqueue `object_delete` for keys; write audit; delete batch row; commit.

`PurgeExpiredAsync` selects from the **small** `trash_batches` table by `DeletedAt < cutoff` (not `MIN(DeletedAt)` over files). Idempotent if rows already partially gone.

## Store reconciliation

`IStoreReconciliationService` / CLI `reconcile-store`:

- Compares DB object keys vs store listing.
- **Reports only** orphans and missing objects — does not auto-delete in the default path.
- Used after drills and as an operator health check; deletion tests assert `IsClean` after discard/purge scenarios.

## Backups (background + CLI)

`BackupOptions`: root path, retain daily/weekly/monthly, `pg_dump`/`pg_restore` paths, low-disk guard, optional admin restore.

- Hosted runner and/or CLI `backup` / `restore` / `restore-drill`.
- Manifest includes object keys + checksums for verification.
- **HMAC keys for public links are not inside DB dumps** — restore separately (operator guide).

## Failure modes

| Failure | Expected handling |
|---------|-------------------|
| Object delete transient error | Retry with backoff until max attempts → `dead` |
| Purge crash mid-batch | Retry `PurgeBatchAsync`; finish remaining rows |
| Quota drift | Reconciliation job corrects counters |
| User delete stuck in `personal`+ | Sweep advances CAS phases |
| Statement timeout on team delete | Full txn rollback; team remains |
