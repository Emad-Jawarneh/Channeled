# Channeled Progress

## Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0 Foundations | ✅ Complete | `./ci.sh` green |
| M1 Identity | ✅ Complete | Auth abuse API tests |
| M2 Teams and channels | ✅ Complete | Authorizer matrix 160+ cases; API 404 for hidden team/channel resources; `./ci.sh` green |
| M3 Files core | ⏸ Next | — |
| M4–M8 | ⏸ | — |

## M2 exit gate

- [x] Teams, membership, roles, ownership rules
- [x] Channels (open/private), private membership
- [x] Single `IAuthorizer` evaluator
- [x] Table-driven unit test for role × visibility × action matrix
- [x] API tests: 404 for hidden resources (outsider + private channel)
- [x] Deny-by-default (not 403 for missing team/channel access)

## Verified

```text
dotnet test tests/Channeled.Tests.Unit --filter Authorizer
# Passed: 163

dotnet test tests/Channeled.Tests.Api
# Passed: 13

./ci.sh
# === CI green ===
```
