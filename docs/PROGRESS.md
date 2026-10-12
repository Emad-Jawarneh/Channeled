# Channeled Progress

## Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0 Foundations | ✅ Complete | `./ci.sh` green |
| M1 Identity | ✅ Complete | Auth abuse API tests |
| M2 Teams and channels | ✅ Complete | Authorizer matrix; API 404; `./ci.sh` green |
| M3 Files core | ✅ Complete | 2 GB gate; resume; SHA-256; `./ci.sh` green |
| M4 Personal space | ✅ Complete | Isolation API tests (other user + platform admin); copy-to-channel; `./ci.sh` green |
| M5 Search and metadata | ⏸ Next | — |
| M6–M8 | ⏸ | — |

## M4 exit gate

- [x] My files, folders, recent, starred
- [x] Copy to channel (new row + new object)
- [x] Personal quota enforcement
- [x] Isolation: non-owner and platform admin cannot reach personal content
- [x] `./ci.sh` green

## Verified

```text
dotnet test tests/Channeled.Tests.Api --filter PersonalIsolation
# Passed: 4

./ci.sh
# === CI green ===
# unit 193 · integration 4 · API 20
```
