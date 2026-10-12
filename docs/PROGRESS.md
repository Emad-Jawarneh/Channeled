# Channeled Progress

## Current focus: M0 Foundations

### Plan

1. Reset dishonest scaffold; keep `docs/AGENTS.md` + wireframes.
2. Create solution + test projects (.NET 10 LTS, nullable, warnings as errors).
3. Application ports: `IFileStore`, `IClock`; Infrastructure filesystem + S3 adapters.
4. Minimal API (health + OpenAPI) and OpenAPI → TS client pipeline.
5. `/web` Vite scaffold (strict TS, Router, Query, Radix, CSS modules, tokens).
6. Fail-closed `ci.sh`; native Postgres/MinIO install scripts (no Docker).
7. Integration test: round-trip object through filesystem store (always); S3 when MinIO is up.
8. Gate: `./ci.sh` green; update this file with evidence.

### Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0 Foundations | 🔄 In progress | — |
| M1–M8 | ⏸ Blocked on M0 | — |

### Verified

_(filled as gates pass)_

### Open risks

- npm may need install on this machine for `/web` CI steps.
- MinIO may be absent; S3 integration test skips unless `CHANNELED_S3_ENDPOINT` is set.
