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
