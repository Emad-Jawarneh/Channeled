# Operator Guide (M0 draft)

Native install only — no Docker.

## Prerequisites

- .NET 10 SDK
- Node.js 20+ with npm (for `/web`)
- PostgreSQL 16 (native)
- MinIO native binary (optional for M0; filesystem store works without it)

## Quick local API

```bash
dotnet run --project src/Channeled.Api
curl http://localhost:5000/health
```

## Postgres

```bash
./ops/postgresql/install.sh
```

## MinIO

```bash
./ops/minio/install.sh
```

Set env overrides (never commit secrets):

```bash
export ConnectionStrings__Postgres='Host=localhost;Database=channeled;Username=channeled;Password=…'
export FileStore__Type=S3Compatible
export FileStore__Endpoint=http://127.0.0.1:9000
export FileStore__AccessKey=…
export FileStore__SecretKey=…
export FileStore__Bucket=channeled
```

## CI

```bash
./ci.sh
```

Must exit non-zero on any failure.
