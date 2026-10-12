# Channeled Progress

## Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0–M4 | ✅ Complete | Prior gates |
| M5 Search and metadata | ✅ Complete | Trigram search; usage; reconcile; 1M p95 ≤ 500 ms; `team_quota_exceeded`; `./ci.sh` green |
| M6 Administration | ⏸ Next | — |
| M7–M8 | ⏸ | — |

## M5 exit gate

- [x] Trigram search per scope (`channel` / `team` / `personal`) + filters
- [x] Usage reporting (`GET /api/v1/teams/{slug}/storage`, `/me/storage`)
- [x] Quota enforcement (`team_quota_exceeded`) + reconciliation job
- [x] p95 list/search on seeded 1M-file dataset ≤ 500 ms
- [x] `./ci.sh` green

## Seed / bench

```bash
./scripts/seed-bench-files.sh 1000000
# or: dotnet run --project src/Channeled.Api -- seed-bench-files --count 1000000
```

## Verified

```text
dotnet test tests/Channeled.Tests.Integration --filter MillionFileBench
# Passed (1M seed; list+search p95 < 500ms)

dotnet test tests/Channeled.Tests.Api --filter SearchAndQuota
# Passed: 4

./ci.sh
# === CI green ===
# unit 194 · integration 5 · API 24
```
