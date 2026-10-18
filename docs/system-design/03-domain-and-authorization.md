# 03 — Domain & authorization

## Core entities (conceptual)

| Concept | Persistence | Notes |
|---------|-------------|-------|
| User | `AspNetUsers` (`AppUser`) | Identity + `IsPlatformAdmin`, personal quota/usage, `Status`, `DeletePhase` |
| Team | `teams` | Slug, quotas, join mode/token, logo, archive |
| Team membership | `team_members` | Role: Viewer < Member < Admin < Owner |
| Channel | `channels` | `Kind` file\|text (immutable); `Visibility` everyone\|restricted |
| Channel grant | `channel_members` | Restricted channels; `ReadOnly` means view-only write flag inverted in code |
| Folder / file | `folders`, `files` | Exactly one scope: `ChannelId` **xor** `OwnerUserId` |
| Trash batch | `trash_batches` | Soft-delete grouping; retention clock |
| Message | `channel_messages` | Soft-delete; optional reply + file refs |
| Notification | `notifications` | Mention, reply, team join request |
| Share link | `file_share_links` | Token **hash** only; team-scoped |
| Audit | `audit_events` | Append-only; nullable `TeamId` (no FK) |
| Session | `sessions` / user sessions | Revocable; bound into cookie via `sid` |
| Job | `jobs` | DB-backed outbox work |

Keys are UUIDv7; timestamps UTC.

## Team roles

```text
Viewer (0) < Member (1) < Admin (2) < Owner (3)
```

| Capability | Viewer | Member | Admin | Owner |
|------------|:------:|:------:|:-----:|:-----:|
| Browse accessible channels / download / search | ✓ | ✓ | ✓ | ✓ |
| Upload; rename/move/delete **own** files | | ✓ | ✓ | ✓ |
| Rename/move/delete **any** files; moderate messages | | | ✓ | ✓ |
| Create/archive/delete channels; manage members/join | | | ✓ | ✓ |
| Edit team settings (non-delete) | | | ✓ | ✓ |
| Delete team; transfer ownership; join mode | | | | ✓ |

Rules of thumb:

- A team always keeps at least one Owner.
- Admins cannot promote/demote Owners.
- Viewers never write, including in private channels with grants.

## Channel visibility & write

- **Everyone:** all team members at their team role.
- **Restricted:** listed `channel_members` (+ Owners/Admins always see). Write requires a non-read-only grant (or Admin+/Owner via Authorizer).

Channel `Kind` is immutable (DB trigger / domain rule).

## Authorizer (single evaluator)

**Code:** `src/Channeled.Domain/Authorization/Authorizer.cs`  
**Actions:** `AuthAction` enum  
**Context:** `AuthorizationContext` (membership facts assembled per request in Infrastructure)

Contract:

```text
Authorize(context) → Allow | Deny(reasonCode)
```

Properties:

1. **Deny by default** — unknown action/resource combinations fail closed.
2. **One evaluator** — no parallel role matrices in endpoints or React.
3. **Personal space** — `AccessPersonal` / `WritePersonal` require ownership; **platform admin does not bypass**.
4. **Archived** team/channel blocks writes (posts/uploads).
5. Denials for “should not exist to this user” are mapped to **HTTP 404** at the API boundary so existence is not leaked.

UI may hide buttons; only the Authorizer decides.

## Team join

| Mode | Behavior |
|------|----------|
| Open join | Valid link joins immediately (subject to caps) |
| Request to join | Link creates pending request; Admin+ approve/reject |

Join tokens are stored encrypted/hashed (`TeamJoinLinkProtector` + Data Protection). Admin+ can rotate/ensure links; Owner changes join mode. Reject/withdraw apply cooldowns. Large pending queues can block “convert to open” (`TeamJoin:MaxOpenJoinPendingApprove`).

HTTP team **invitation** create/accept paths are retired (410); audit enum values may remain for history.

## Platform admin

Flag: `AppUser.IsPlatformAdmin`.

- Grants `/api/v1/admin/*` only (users, teams quotas, audit, backups, health, purge hooks).
- Does **not** grant personal-file browse/download.
- Last enabled platform admin cannot be deleted (see [07 — Deletion lifecycles](07-deletion-lifecycles.md)).

Bootstrap: CLI `seed-operator` (once) or `ensure-user --admin`.

## Naming & structure invariants

- Sibling names unique among non-deleted items (case-insensitive).
- Folder depth capped (domain rule; typically 8).
- File/folder check constraint: channel scope XOR personal scope.
- Generated `ContainerId = COALESCE(ChannelId, OwnerUserId)` supports shared listing indexes.

## Error codes

Stable strings in `src/Channeled.Domain/Errors/ErrorCodes.cs` (e.g. `not_found`, `invalid_password`, `team_delete_cap_exceeded`, `parent_trashed`, `last_platform_admin`). Clients should branch on `code`, not English messages.
