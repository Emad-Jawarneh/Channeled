# Architectural Decisions

## 2026-10-12 — .NET 10 LTS

**Decision:** Target `net10.0` (SDK 10.0.x on this machine).
**Reason:** Current even-numbered LTS; AGENTS requires current .NET LTS. Pin package versions explicitly (no open ranges).

## 2026-10-12 — Object store primary / alternate

**Decision:** Primary MinIO native binary; alternate SeaweedFS.
**Reason:** AGENTS requires a maintained native binary; no Docker in any path.

## 2026-10-12 — M0 store round-trip gate

**Decision:** Always-on integration test uses the filesystem adapter against a real temp directory. S3 adapter integration runs when `CHANNELED_S3_ENDPOINT` (and credentials) are set.
**Reason:** Proves streaming store contract without blocking CI when MinIO is not installed; S3 path remains tested when ops scripts have started MinIO.

## 2026-10-12 — OpenAPI → TypeScript client

**Decision:** Use ASP.NET Core built-in OpenAPI (`Microsoft.AspNetCore.OpenApi`) + `openapi-typescript` (or NSwag) in `/web` scripts once endpoints exist; M0 ships the pipeline hook and a placeholder generated module.
**Reason:** Spec requires generated client; empty feature set still needs a working generation path.

## 2026-10-12 — Frontend styling

**Decision:** CSS modules + design tokens (CSS variables). No Tailwind.
**Reason:** Matches AGENTS (`plain CSS modules and design tokens`).

## 2026-10-12 — npm audit threshold

**Decision:** CI `npm audit --audit-level=high` (fail on high/critical).
**Reason:** Remaining moderate is `@vitest/mocker` only fixed by Vitest 5 (breaking). Track Vitest 5 upgrade; do not weaken to ignore high.

## 2026-10-12 — OpenAPI package version

**Decision:** `Microsoft.AspNetCore.OpenApi` 10.0.12 (not 10.0.0).
**Reason:** 10.0.0 pulled vulnerable `Microsoft.OpenApi` 2.0.0 (NU1903).

## 2026-10-12 — M1 platform invitations

**Decision:** M1 uses `platform_invitations` (email + hashed token) for account creation before teams exist. Team-scoped invitations remain M2.
**Reason:** AGENTS requires invitation-only signup in M1; team_id invites need M2 teams.

## 2026-10-12 — Open self-registration

**Decision:** `POST /api/v1/auth/register` creates a normal (non-admin) account and signs the user in. Platform and team invitations remain available.
**Reason:** Product requirement that signups are not invitation-only; invitations still cover team join and optional admin-provisioned accounts.

## 2026-10-12 — Channel public join links

**Decision:** Every channel exposes a stable public path `/join/{team}/{channel}`. Team members without private-channel access can create a pending `channel_join_requests` row; users with `ManageChannelMembership` approve (upsert `channel_members`) or reject.
**Reason:** Shareable, non-secret invitation links with human review instead of auto-grant.

## 2026-10-12 — UI mockup system

**Decision:** SPA visual system follows `docs/design/Channeled – UI Mockups.html`: Manrope, ink `#373f51`, soft teal `#66acb7`, rail/sidebar/main/details shell, split auth screens.
**Reason:** Product-provided mockups are the source of truth for layout and brand.

## 2026-10-13 — Dark mode

**Decision:** App-wide light/dark toggle via `data-theme` on `html`, CSS variable swap, preference in `localStorage` (`channeled-theme`), toggle in shell/landing navbars (not a floating overlay).
**Reason:** Readable UI in both environments without maintaining separate style trees.

## 2026-10-13 — Optional multi-team creation

**Decision:** Team creation is optional at welcome (skip to My files). Users may create multiple teams anytime via the rail “+” dialog; API already allowed this.
**Reason:** Personal space should work without a team; orgs often need more than one team.

## 2026-10-13 — File vs text channels

**Decision:** Channels have `Kind` (`File` | `Text`). Sidebar lists them in separate sections. File channels keep uploads/folders; text channels are labeled context spaces only (no messaging product — chat remains out of scope). File APIs reject text-channel targets.
**Reason:** Clear separation of file workspaces from organizational labels without building chat.

## 2026-10-12 — Identity keys

**Decision:** ASP.NET Identity with `Guid` keys generated as UUIDv7 in application code.
**Reason:** Matches domain UUIDv7 requirement; Identity stays in Infrastructure (Domain remains framework-free).

## 2026-10-12 — Single IAuthorizer

**Decision:** All team/channel permission checks go through one `IAuthorizer` implementation; services never branch on role strings ad hoc.
**Reason:** AGENTS M2 gate requires one evaluator and a full role × visibility × action matrix test.

## 2026-10-12 — Deny-by-default as 404

**Decision:** Missing team membership or private-channel access returns `404 NotFound` (not `403`) for team/channel resources.
**Reason:** Spec require deny-by-default that does not leak existence of hidden resources; API tests assert 404 for outsiders.

## 2026-10-12 — M3 upload SHA-256

**Decision:** SHA-256 is computed by streaming the assembled object after multipart complete; optional client `ExpectedSha256Hex` must match or the object is deleted and `checksum_mismatch` returned.
**Reason:** Confirms integrity end-to-end without buffering the file in memory.

## 2026-10-12 — Upload part persistence

**Decision:** Filesystem multipart adapter persists `meta.json` after each part and rebuilds part indexes from `part-*` files on resume.
**Reason:** M3 gate requires kill/resume across process restart.

## 2026-10-12 — Personal space owner-only

**Decision:** `AccessPersonal` / `WritePersonal` authorize solely via `IsPersonalOwner`. `IsPlatformAdmin` never grants personal access. Queries always filter `OwnerUserId == actor`.
**Reason:** M4 gate — no other user including platform admin may reach personal content; admin is for `/admin/*` only.

## 2026-10-12 — Copy to channel

**Decision:** Copy streams the source object to a new `ObjectKey` and inserts a channel-scoped file row (independent bytes + metadata); audit `FileCopy` records source and target ids.
**Reason:** Spec requires an independent server-side copy, not a shared reference.

## 2026-10-12 — M5 search and bench target

**Decision:** File/channel name search uses PostgreSQL `pg_trgm` GIN indexes and `ILIKE`/`similarity()` ranking. M5 gate p95 for list and search on a 1M-file seed is **≤ 500 ms** (`SearchBench:P95MaxMilliseconds`). Seed via `scripts/seed-bench-files.sh` or `dotnet run … seed-bench-files`.
**Reason:** AGENTS M5 gate requires measured p95 under a stated target on a scripted 1M dataset.

## 2026-10-12 — Quota reconciliation

**Decision:** Nightly hosted job recomputes `teams.UsageBytes` and `AspNetUsers.PersonalUsageBytes` as `SUM(files.SizeBytes)` including trashed rows (trash counts until purge). Manual trigger: `POST /api/v1/admin/jobs/quota-reconcile` (platform admin).
**Reason:** Counters stay correct after drift; trash still occupies quota per domain rules.

## 2026-10-12 — Audit append-only

**Decision:** `audit_events` is append-only for the app role: BEFORE UPDATE/DELETE triggers raise; `REVOKE UPDATE, DELETE ON audit_events FROM channeled`. INSERT/SELECT remain.
**Reason:** M6 gate — audit must be immutable for the application connection; DB enforces it even if app code is wrong.

## 2026-10-12 — Admin surface vs personal

**Decision:** Platform admin APIs cover dashboard, users, team quotas, audit export, health, and backup/config/purge/download audit stubs. Personal space remains owner-only; admin never bypasses personal authorization.
**Reason:** AGENTS: `/admin/*` only for platform admin; personal isolation from M4 stands.

## 2026-10-12 — Backup package format

**Decision:** Each backup is a directory: `database.dump` (`pg_dump -Fc`), `objects/` (keyed files), `manifest.json` (key, size, SHA-256). Restore filters EXTENSION TOC entries so non-superuser `pg_restore` works when `pg_trgm` is owned by postgres.
**Reason:** M7 requires consistent DB + object + manifest with verification; app role typically cannot DROP EXTENSION.

## 2026-10-12 — Admin restore in test hosts

**Decision:** `Backup:RestoreViaAdmin` defaults true in production; false under Testing / API test factory. Catalog tests audit `BackupRestore` without running `pg_restore` against the shared live pool.
**Reason:** Full restore terminates backends and is covered by `RestoreDrillTests` / `ops/backup/restore-drill.sh`.

## 2026-10-12 — M8 SPA in Hardening

**Decision:** Full SPA (rail/sidebar/main/details) ships in M8 alongside Playwright e2e, because earlier milestones delivered API-first surfaces and AGENTS places Playwright in M8.
**Reason:** Wireframe routes and e2e flows require UI; M7 was operations-only.

## 2026-10-12 — Load test profiles

**Decision:** CI runs a reduced concurrency/latency profile (`LOAD_USERS`/`LOAD_ITERATIONS`). Full AGENTS scale (200 users, 50 teams, 1M files, ~2 TB) is documented and operator-run after bench seed; object corpus for 2 TB is not required for the latency harness.
**Reason:** CI machines cannot hold a 2 TB store; metadata bench + concurrent list/search still validates scale path.
