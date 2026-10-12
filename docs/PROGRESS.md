# Channeled Progress

## Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0–M5 | ✅ Complete | Prior gates |
| M6 Administration | ✅ Complete | `AdminCatalogAndImmutabilityTests`; `./ci.sh` green |
| M7–M8 | ⏸ | — |

## M6 exit gate

- [x] Dashboard, users/teams management, audit viewer + CSV/JSON export
- [x] Every audit catalog event emitted and asserted (`Every_audit_catalog_event_is_emitted`)
- [x] `audit_events` rejects UPDATE/DELETE for app role (trigger + REVOKE; immutability tests)
- [x] `./ci.sh` green

## M6 notes

- `AuditEventType` + `AuditCatalog` cover the AGENTS catalog
- `IAdminService` / `/api/v1/admin/*`: dashboard, health, users CRUD/disable/reset, teams list/quota, audit list/export, backup stubs, config/download/purge audit hooks
- Migration `AuditAppendOnly`: deny UPDATE/DELETE triggers on `audit_events`; REVOKE for role `channeled`
- Team invite revoke + channel update; auth change-password emit catalog events
