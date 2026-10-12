# Channeled Progress

## Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0 Foundations | ✅ Complete | `./ci.sh` exit 0 (2026-10-12). Build 0 warnings. Unit 4, Integration 2 (filesystem round-trip), API 1. Web typecheck/lint/test/build + OpenAPI client gen. Audits: no high+. |
| M1 Identity | ⏸ Next | — |
| M2–M8 | ⏸ | — |

## M0 exit gate

- [x] Solution + `/web` scaffold
- [x] Fail-closed `./ci.sh`
- [x] Native Postgres / MinIO install scripts (no Docker)
- [x] `IFileStore` filesystem + S3 adapters
- [x] Design tokens + OpenAPI → TS client pipeline (`openapi/v1.json` → `web/src/api/generated.ts`)
- [x] Integration round-trip through real filesystem store
- [x] S3 round-trip test present (runs when `CHANNELED_S3_ENDPOINT` set)

## Verified commands

```text
./ci.sh
# === CI green ===
dotnet test Channeled.slnx -c Release
# Unit 4, Integration 2, Api 1 — all passed
```

## Open risks / follow-ups

- `npm audit` still reports moderate `@vitest/mocker` (fix is Vitest 5 breaking). CI fails on high+. Track bump to Vitest 5 in hardening.
- MinIO not installed on this machine yet; S3 integration path is env-gated.
- No EF/Identity yet (M1). Domain stays framework-free.
