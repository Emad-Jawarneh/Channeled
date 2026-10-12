# Channeled

Self-hosted, multi-team file-sharing platform.

**Platform → Team → Channel → File** (plus personal space). Native install only — **no Docker**.

| Doc | Purpose |
|-----|---------|
| [`docs/AGENTS.md`](docs/AGENTS.md) | Product / engineering spec |
| [`docs/PROGRESS.md`](docs/PROGRESS.md) | Milestone status and gate evidence |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Architectural decisions |
| [`docs/OPERATOR_GUIDE.md`](docs/OPERATOR_GUIDE.md) | Ops install notes |
| [`docs/SECURITY.md`](docs/SECURITY.md) | OWASP ASVS L2 checklist |

## Status

**M0–M8 complete** — all AGENTS milestones green (SPA, Playwright e2e + axe, ASVS L2, load profile, ops).

## Stack

- Backend: .NET 10 LTS (`net10.0`), EF Core + PostgreSQL, ASP.NET Identity (cookies + CSRF)
- Frontend: React + TypeScript (strict) + Vite (`/web`)
- Object store: filesystem adapter by default; S3-compatible (MinIO) optional
- CI: fail-closed [`./ci.sh`](ci.sh) (includes Playwright + reduced load test)

## Prerequisites

- .NET 10 SDK
- Node.js 20+ with npm (nvm recommended)
- PostgreSQL 16+ (`pg_dump` / `pg_restore` on `PATH`)
- Optional: MinIO native binary; Playwright OS libs (`npx playwright install-deps` with sudo)

```bash
./ops/postgresql/install.sh   # needs sudo + CHANNELED_DB_PASSWORD
./ops/minio/install.sh        # optional
```

Default connection (override with env):

```text
Host=127.0.0.1;Port=5432;Database=channeled;Username=channeled;Password=channeled_dev
```

## Quick start (clean machine)

### 1. API

```bash
cd /path/to/Channeled
dotnet run --project src/Channeled.Api --launch-profile http
```

- Listens on **http://localhost:5048**
- Applies EF migrations on startup (`MigrateOnStartup`)
- Health: `curl http://localhost:5048/api/v1/health`
- Metrics: `curl http://localhost:5048/metrics`

Seed the first operator (once):

```bash
dotnet run --project src/Channeled.Api -- seed-operator \
  --email you@example.com \
  --password 'YourPassword1!' \
  --name Operator
```

### 2. Web

```bash
export NVM_DIR="$HOME/.nvm" && . "$NVM_DIR/nvm.sh"   # if you use nvm
cd web && npm ci && npm run dev
```

Open the Vite URL (proxies `/api` to `:5048`) → **Sign in** → create a team → browse `#general`.

Routes include `/me/files`, `/search`, `/t/:team/settings/*`, `/admin/*`, `/invite/:token`, `/reset/:token`.

### 3. API usage (cookie + CSRF)

```bash
# Login
curl -c cookies.txt -X POST http://localhost:5048/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"YourPassword1!"}'

# CSRF
TOKEN=$(curl -s -b cookies.txt http://localhost:5048/api/v1/auth/csrf | jq -r .token)

# Example: list teams
curl -s -b cookies.txt http://localhost:5048/api/v1/teams/ | jq .
```

Mutating requests need the `X-CSRF-TOKEN` header after `/auth/csrf`.

| Area | Prefix |
|------|--------|
| Auth | `/auth/login`, `/auth/me`, `/auth/csrf` |
| Teams / channels | `/teams/…` |
| Channel files | `/teams/{slug}/channels/{name}/items`, `/uploads` |
| Personal | `/me/files/…` |
| Search | `/search?q=…&scope=…` |
| Admin | `/admin/dashboard`, `/admin/users`, `/admin/audit`, `/admin/backups` |

## CI

```bash
./ci.sh
```

Fail-closed: restore/build, unit + integration + API tests, web typecheck/lint/test/build, OpenAPI → TS, dependency audits, format, **Playwright e2e + axe**, reduced **load test**.

Standalone:

```bash
./scripts/e2e.sh
LOAD_USERS=5 LOAD_ITERATIONS=10 node scripts/load-test.mjs
```

## Backups / restore drill (M7)

```bash
./ops/backup/backup.sh ondemand
./ops/backup/restore.sh <backup-guid>
./ops/backup/restore-drill.sh
```

## Search bench (M5)

```bash
./scripts/seed-bench-files.sh 1000000
```

## Configuration

```bash
export ConnectionStrings__Postgres='Host=localhost;Database=channeled;Username=channeled;Password=…'
export FileStore__Kind=FileSystem   # or S3Compatible
export FileStore__RootPath=.data/object-store
export Backup__RootPath=.data/backups
export Backup__AlertEmail=ops@example.com
```

See [`ops/channeled.env.example`](ops/channeled.env.example) and [`docs/OPERATOR_GUIDE.md`](docs/OPERATOR_GUIDE.md).

## Repo layout

```text
/src/Channeled.Domain|Application|Infrastructure|Api
/tests/Channeled.Tests.Unit|Integration|Api
/web                 React SPA + Playwright e2e
/ops                 Native install, systemd, backup, proxy samples
/docs                Spec, progress, decisions, operator guide, security
/scripts             seed-bench, e2e, load-test, OpenAPI client
/openapi             OpenAPI document for client generation
```
