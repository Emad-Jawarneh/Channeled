# Channeled

Self-hosted, multi-team file-sharing platform. Hierarchy: Platform → Team → Channel → File.

Spec: [`docs/AGENTS.md`](docs/AGENTS.md). Progress: [`docs/PROGRESS.md`](docs/PROGRESS.md).

## Stack

- .NET 10 LTS (`net10.0`), React + TypeScript (strict), PostgreSQL, MinIO (native)
- No Docker anywhere

## Quick start (dev)

```bash
# API
dotnet run --project src/Channeled.Api

# Web (separate terminal; uses nvm Node if installed)
export NVM_DIR="$HOME/.nvm" && . "$NVM_DIR/nvm.sh"
cd web && npm ci && npm run dev
```

Health: `http://localhost:5000/health`

## CI

```bash
./ci.sh
```

Fail-closed: build, unit/integration/API tests, web typecheck/lint/test/build, OpenAPI client generation, audits.

## M0 status

Foundations: solution layout, `IFileStore` (filesystem + S3), fail-closed CI, web scaffold, store round-trip integration test.
