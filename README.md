# Channeled

Self-hosted, multi-team file-sharing platform.

**Platform → Team → Channel → File** (plus personal space). Native install only — **no Docker**.

| Doc | Purpose |
|-----|---------|
| [`docs/AGENTS.md`](docs/AGENTS.md) | Product / engineering spec |
| [`docs/PROGRESS.md`](docs/PROGRESS.md) | Milestone status and gate evidence |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Architectural decisions |
| [`docs/OPERATOR_GUIDE.md`](docs/OPERATOR_GUIDE.md) | Ops install notes |

## Status

**M0–M5 complete** (foundations, identity, teams/channels, files, personal space, search/quotas). Next: **M6 Administration**.

The web UI today is mainly **login + health**. Teams, files, personal space, and search are available via the **API** (cookie auth).

## Stack

- Backend: .NET 10 LTS (`net10.0`), EF Core + PostgreSQL, ASP.NET Identity (cookies + CSRF)
- Frontend: React + TypeScript (strict) + Vite (`/web`)
- Object store: filesystem adapter by default; S3-compatible (MinIO) optional
- CI: fail-closed [`./ci.sh`](ci.sh)

## Prerequisites

- .NET 10 SDK
- Node.js 20+ (for `/web` and CI)
- PostgreSQL 16+ with a database/user (defaults below)
- Optional: MinIO native binary

```bash
# Optional native install stubs
./ops/postgresql/install.sh
./ops/minio/install.sh
```

Default connection (override with env):

```text
Host=127.0.0.1;Port=5432;Database=channeled;Username=channeled;Password=channeled_dev
```

## Quick start

### 1. API

```bash
cd /path/to/Channeled
dotnet run --project src/Channeled.Api --launch-profile http
```

- Listens on **http://localhost:5048**
- Applies EF migrations on startup (`MigrateOnStartup`)
- Health: `curl http://localhost:5048/api/v1/health`

Seed the first operator (once):

```bash
dotnet run --project src/Channeled.Api -- seed-operator \
  --email you@example.com \
  --password 'YourPassword1!' \
  --name Operator
```

### 2. Web (optional)

```bash
export NVM_DIR="$HOME/.nvm" && . "$NVM_DIR/nvm.sh"   # if you use nvm
cd web && npm ci && npm run dev
```

Open the Vite URL → `/login` (proxies API to `:5048`).

### 3. API usage (cookie + CSRF)

```bash
# Login
curl -c cookies.txt -b cookies.txt -X POST http://localhost:5048/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"YourPassword1!"}'

TOKEN=$(curl -s -c cookies.txt -b cookies.txt http://localhost:5048/api/v1/auth/csrf | jq -r .token)

# Create team + open channel
curl -c cookies.txt -b cookies.txt -X POST http://localhost:5048/api/v1/teams/ \
  -H "Content-Type: application/json" -H "X-CSRF-TOKEN: $TOKEN" \
  -d '{"name":"Acme","description":"demo"}'

curl -c cookies.txt -b cookies.txt -X POST http://localhost:5048/api/v1/teams/acme/channels \
  -H "Content-Type: application/json" -H "X-CSRF-TOKEN: $TOKEN" \
  -d '{"name":"general","visibility":0}'
```

Useful groups (all under `/api/v1`):

| Area | Examples |
|------|----------|
| Auth | `/auth/login`, `/auth/me`, `/auth/csrf` |
| Teams / channels | `/teams/…`, `/teams/{slug}/channels` |
| Channel files | `/teams/{slug}/channels/{name}/items`, `/uploads` |
| Personal | `/me/files/…`, `/me/storage` |
| Search | `/search?q=…&scope=channel\|team\|personal` |
| Usage | `/teams/{slug}/storage` |

Mutating requests need the `X-CSRF-TOKEN` header after `/auth/csrf`.

## Configuration

Environment overrides (never commit secrets):

```bash
export ConnectionStrings__Postgres='Host=localhost;Database=channeled;Username=channeled;Password=…'
export FileStore__Kind=S3Compatible          # or FileSystem
export FileStore__RootPath=.data/object-store
export FileStore__Endpoint=http://127.0.0.1:9000
export FileStore__AccessKey=…
export FileStore__SecretKey=…
export FileStore__Bucket=channeled
```

## CI

```bash
./ci.sh
```

Fail-closed: restore/build, unit + integration + API tests, web typecheck/lint/test/build, OpenAPI → TS client, dependency audits, format check.

## Repo layout

```text
/src/Channeled.Domain|Application|Infrastructure|Api
/tests/Channeled.Tests.Unit|Integration|Api
/web                 React app
/ops                 Native install stubs (PostgreSQL, MinIO, systemd units)
/docs                Spec, progress, decisions, operator guide, wireframes
/scripts             Helpers (e.g. seed-bench-files.sh)
/openapi             OpenAPI document for client generation
```

## Search bench (M5)

```bash
./scripts/seed-bench-files.sh 1000000
# or: dotnet run --project src/Channeled.Api -- seed-bench-files --count 1000000
```

Integration gate: `dotnet test tests/Channeled.Tests.Integration --filter MillionFileBench` (p95 list/search ≤ 500 ms on 1M rows).
