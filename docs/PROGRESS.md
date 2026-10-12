# Channeled Progress

## Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0 Foundations | ✅ Complete | `./ci.sh` green |
| M1 Identity | ✅ Complete | Auth abuse API tests |
| M2 Teams and channels | ✅ Complete | Authorizer matrix; API 404; `./ci.sh` green |
| M3 Files core | ✅ Complete | Multipart resume; 2 GB constant-memory gate; SHA-256; API upload/trash; `./ci.sh` green |
| M4 Personal space | ⏸ Next | — |
| M5–M8 | ⏸ | — |

## M3 exit gate

- [x] Multipart resumable upload (start / part / complete / abort / list parts)
- [x] Browse/sort/filter/cursor pagination
- [x] Download with ranges
- [x] Rename/move/delete; trash/restore; folders; name conflicts
- [x] Upload + download 2 GB with bounded working set; kill/resume; SHA-256 verified
- [x] `./ci.sh` green

## Verified

```text
dotnet test tests/Channeled.Tests.Integration --filter Two_gigabyte
# Passed (2 GiB multipart + SHA-256 + range; WS delta < 256 MiB)

dotnet test tests/Channeled.Tests.Integration --filter Multipart_survives
# Passed (resume across new store instance)

dotnet test tests/Channeled.Tests.Api
# Passed: 16

./ci.sh
# === CI green ===
```
