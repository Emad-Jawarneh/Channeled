# Docker deployment

Optional path. Native install (systemd / Windows service) remains fully supported — see [`../OPERATOR_GUIDE.md`](../OPERATOR_GUIDE.md).

**Railway:** this Dockerfile is Compose-only (`ops/docker/Dockerfile`). Production on Railway stays on **Railpack** via [`../../railway.toml`](../../railway.toml). Keep the Dockerfile out of the repo root so Railway does not auto-select Docker. Wire Postgres with `DATABASE_URL=${{Postgres.DATABASE_URL}}` on the app service (see Operator Guide).

## What you get

| Service | Role |
|---------|------|
| `postgres` | PostgreSQL 16 |
| `api` | Channeled API + built SPA (`wwwroot`) on port **8080** |
| `minio` + `minio-init` | Optional S3 store (`--profile minio`) |

Persistent volumes: Postgres data, filesystem object store, backups, ASP.NET Data Protection keys.

## Quick start (filesystem store)

```bash
cp ops/docker/compose.env.example ops/docker/compose.env
# Edit POSTGRES_PASSWORD and PUBLIC_URL at minimum.

docker compose --env-file ops/docker/compose.env up -d --build
```

Open http://localhost:8080 (or your `CHANNELED_HTTP_PORT`).

Seed the first platform admin (once):

```bash
docker compose --env-file ops/docker/compose.env exec api \
  dotnet Channeled.Api.dll seed-operator \
  --email you@example.com \
  --password 'YourPassword1!' \
  --name Operator
```

Health: `curl -fsS http://localhost:8080/api/v1/health`

## MinIO profile

```bash
# In compose.env:
# FILE_STORE_KIND=S3Compatible
# FILE_STORE_ENDPOINT=http://minio:9000
# FILE_STORE_ACCESS_KEY=…   # same as MINIO_ROOT_USER
# FILE_STORE_SECRET_KEY=…   # same as MINIO_ROOT_PASSWORD
# MINIO_ROOT_PASSWORD=…

docker compose --env-file ops/docker/compose.env --profile minio up -d --build
```

## TLS / reverse proxy

Point Caddy or nginx at `127.0.0.1:${CHANNELED_HTTP_PORT}` and set:

- `PUBLIC_URL=https://your.domain`
- Matching `Realtime__AllowedOrigins` (compose already sets origin 0 from `PUBLIC_URL`)

Samples: [`../caddy/Caddyfile`](../caddy/Caddyfile), [`../nginx/channeled.conf`](../nginx/channeled.conf).

## Useful commands

```bash
docker compose --env-file ops/docker/compose.env logs -f api
docker compose --env-file ops/docker/compose.env exec api \
  dotnet Channeled.Api.dll reconcile-store
docker compose --env-file ops/docker/compose.env down          # keep volumes
docker compose --env-file ops/docker/compose.env down -v       # wipe data
```

## Image build only

```bash
docker build -t channeled:local .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__Postgres='Host=host.docker.internal;…' \
  -e DataProtection__KeysPath=/data/dp-keys \
  -v channeled-data:/data \
  channeled:local
```

## Notes

- `MigrateOnStartup=true` by default in the image.
- Data Protection keys live under `/app/data/dp-keys` (Compose sets `DataProtection__KeysPath`) so auth cookies and join-link tokens survive recreating the container.
- The image entrypoint binds `ASPNETCORE_URLS` to `$PORT` (default 8080), so the same Dockerfile is safe on PaaS if you ever switch builders.
- Public file link HMAC keys are **not** in Postgres dumps — keep `PUBLIC_FILE_LINKS_HMAC_KEY` in your secret store.
- Playwright / `./ci.sh` still expect a native (or separately provisioned) Postgres; Docker Compose is for **running** the product, not replacing CI.
