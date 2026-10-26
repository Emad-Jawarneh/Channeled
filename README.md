# Channeled
<img width="1512" height="581" alt="image" src="https://github.com/user-attachments/assets/0fc3aeff-72ff-4604-b99d-65bb48133ba5" />

Self-hosted file sharing for teams. Users belong to many teams, work in **file** or **text** channels, keep a private **My files** space, and optionally share anonymous file links. One API process, PostgreSQL, and a local or S3-compatible object store.

Deploy **natively** (recommended for production) or with **Docker Compose** (see below).

```text
Platform → Team → Channel (file | text) → File / Message
                ↘ Personal space (per user)
```

## Features

- Cookie auth + CSRF; revocable sessions; platform admin console
- Team roles (Viewer → Owner), open or request-to-join links, private channels
- Multipart upload through the API, keyset browsing, trash with retention, quotas
- Text channels with mentions, notifications, and SignalR updates
- Authenticated download tickets; optional public share links (off by default)
- Soft delete → purge → `object_delete` outbox; audited mutations
- Native backups (`pg_dump`), store reconcile, systemd / Caddy / nginx samples

## Stack

| Layer | Technology |
|-------|------------|
| API | .NET 10, ASP.NET Core minimal APIs, SignalR |
| Auth | ASP.NET Identity (HttpOnly cookies, SameSite=Lax) |
| Data | EF Core + PostgreSQL 15+ (16+ recommended) |
| Bytes | Filesystem store (default) or S3-compatible (e.g. MinIO) |
| SPA | React 19, TypeScript, Vite, TanStack Query (`web/`) |
| Tests | xUnit, Vitest, Playwright; fail-closed [`./ci.sh`](ci.sh) |

## Prerequisites

- .NET 10 SDK  
- Node.js 20+ and npm  
- PostgreSQL with `pg_dump` / `pg_restore` on `PATH`  
- Optional: MinIO; Playwright OS deps (`npx playwright install-deps`)

```bash
./ops/postgresql/install.sh    # sudo + CHANNELED_DB_PASSWORD
./ops/minio/install.sh         # optional
```

Default DB (override with env):

```text
Host=127.0.0.1;Port=5432;Database=channeled;Username=channeled;Password=channeled_dev
```

## Quick start

### 1. API

```bash
dotnet run --project src/Channeled.Api --launch-profile http
```

- http://localhost:5048  
- Migrations apply on startup when `MigrateOnStartup` is true  
- Health: `GET /api/v1/health` · Metrics: `GET /metrics`

First platform admin (once):

```bash
dotnet run --project src/Channeled.Api -- seed-operator \
  --email you@example.com \
  --password 'YourPassword1!' \
  --name Operator
```

### 2. SPA

```bash
cd web && npm ci && npm run dev
```

Open the Vite URL (proxies `/api` to the API). Sign in → create a team → open a channel.

Useful routes: `/me/files`, `/search`, `/t/:team/…`, `/admin/*`, `/reset/:token`.

### 3. Docker Compose (optional)

```bash
cp ops/docker/compose.env.example ops/docker/compose.env
# Set POSTGRES_PASSWORD (and PUBLIC_URL if not localhost).

docker compose --env-file ops/docker/compose.env up -d --build

docker compose --env-file ops/docker/compose.env exec api \
  dotnet Channeled.Api.dll seed-operator \
  --email you@example.com \
  --password 'YourPassword1!' \
  --name Operator
```

App: http://localhost:8080 · Guide: [`ops/docker/README.md`](ops/docker/README.md) · MinIO: add `--profile minio` and set `FILE_STORE_KIND=S3Compatible` in `compose.env`.

### 4. Call the API (cookie + CSRF)

```bash
curl -c cookies.txt -X POST http://localhost:5048/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"YourPassword1!"}'

TOKEN=$(curl -s -b cookies.txt http://localhost:5048/api/v1/auth/csrf | jq -r .token)

curl -s -b cookies.txt -H "X-CSRF-TOKEN: $TOKEN" \
  http://localhost:5048/api/v1/teams/ | jq .
```

Unsafe methods require `X-CSRF-TOKEN` after `/api/v1/auth/csrf`.

| Area | Under `/api/v1` |
|------|-----------------|
| Auth | `/auth/login`, `/auth/me`, `/auth/csrf`, `/auth/sessions` |
| Teams | `/teams/…` (members, channels, join) |
| Files | `/teams/{slug}/channels/{name}/items`, `/uploads` |
| Personal | `/me/files/…` |
| Chat | text-channel message routes |
| Search | `/search` |
| Admin | `/admin/…` (platform admin only) |
| Realtime | SignalR hub `/hubs/channel` (WebSockets) |

OpenAPI: [`openapi/v1.json`](openapi/v1.json).

## Configuration

Env overrides (see [`ops/channeled.env.example`](ops/channeled.env.example)):

```bash
export ConnectionStrings__Postgres='Host=localhost;Database=channeled;Username=channeled;Password=…'
export FileStore__Kind=FileSystem          # or S3Compatible
export FileStore__RootPath=.data/object-store
export Backup__RootPath=.data/backups
export PublicFileLinks__Enabled=false
```

Full knob list: [`docs/OPERATOR_GUIDE.md`](docs/OPERATOR_GUIDE.md) and [`docs/system-design/09-admin-ops-and-config.md`](docs/system-design/09-admin-ops-and-config.md).

## Docs

| Doc | What it is |
|-----|------------|
| [`docs/system-design/`](docs/system-design/README.md) | As-built architecture, deletion lifecycles, [ERD/DFD/UML](docs/system-design/11-diagrams-erd-dfd-uml.md) |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Dated architectural decisions |
| [`docs/OPERATOR_GUIDE.md`](docs/OPERATOR_GUIDE.md) | Install, systemd, backup, CLI |
| [`docs/SECURITY.md`](docs/SECURITY.md) | OWASP ASVS L2 checklist |
| [`docs/AGENTS.md`](docs/AGENTS.md) | Original product / engineering prompt |
| [`docs/PROGRESS.md`](docs/PROGRESS.md) | Milestone gate history |

## Repo layout

```text
src/Channeled.Domain|Application|Infrastructure|Api
tests/Channeled.Tests.Unit|Integration|Api
web/                 SPA + Playwright
ops/                 Postgres/MinIO install, backup, proxy, systemd, docker/
docs/                Spec, design, security, operator guide
scripts/             e2e, load test, bench seed, OpenAPI client
openapi/             Published OpenAPI document
ops/docker/Dockerfile  Multi-stage API + SPA image (Compose; Railway uses Railpack)
docker-compose.yml   Postgres + API (+ optional MinIO profile)
ci.sh                Fail-closed CI entrypoint
```

## Development commands

```bash
./ci.sh                                              # full gate
./scripts/e2e.sh                                     # Playwright
LOAD_USERS=5 LOAD_ITERATIONS=10 node scripts/load-test.mjs
./ops/backup/backup.sh ondemand
./ops/backup/restore-drill.sh
./scripts/seed-bench-files.sh 1000000                # search bench corpus
```

Operator CLI (API project):

```bash
dotnet run --project src/Channeled.Api -- ensure-user --email … --password … [--admin]
dotnet run --project src/Channeled.Api -- delete-user --actor-email … --actor-password … --email …
dotnet run --project src/Channeled.Api -- backup | restore | reconcile-store | restore-drill
```

## Design principles (short)

1. **One authorizer** — deny by default; hidden resources return **404**, not 403.  
2. **No store credentials in the browser** — all bytes stream through the API.  
3. **PostgreSQL is metadata truth** — object keys are opaque; names never appear in keys.  
4. **Soft delete first** — trash retention, then purge + outbox byte deletion.  
5. **Audit in the same transaction** as the change.  
6. **Personal space is owner-only** — platform admin cannot browse personal file content via API.

## License

Proprietary / unlicensed unless a license file is added to this repository.
