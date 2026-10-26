# Channeled — System design

This folder is the **as-built** system design for Channeled: how the running product is structured, what invariants it enforces, and where to look in the code. It complements (does not replace) the product/build prompt and ops docs.

| Document | Contents |
|----------|----------|
| [01 — Overview](01-overview.md) | Product model, stack, deployment shape, doc map |
| [02 — Architecture & layering](02-architecture-and-layering.md) | Solution layout, DI, request path, API surface |
| [03 — Domain & authorization](03-domain-and-authorization.md) | Roles, Authorizer, teams/channels, deny→404 |
| [04 — Identity & sessions](04-identity-and-sessions.md) | Cookies, CSRF, sessions, account flows |
| [05 — Files, storage & quotas](05-files-storage-and-quotas.md) | Uploads, listing, trash, downloads, shares, personal |
| [06 — Chat, notifications & realtime](06-chat-notifications-realtime.md) | Text channels, mentions, SignalR |
| [07 — Deletion lifecycles](07-deletion-lifecycles.md) | Team delete, admin user delete, folder trash |
| [08 — Jobs & background work](08-jobs-and-background.md) | Outbox, hosted services, reconcile |
| [09 — Admin, ops & configuration](09-admin-ops-and-config.md) | Admin API, CLI, config knobs, backups |
| [10 — Frontend](10-frontend.md) | SPA structure, auth client, realtime, patterns |
| [11 — Diagrams (ERD, DFD, UML)](11-diagrams-erd-dfd-uml.md) | Entity-relationship, data-flow, component/class/sequence/state |

## Related docs (outside this folder)

| Doc | Role |
|-----|------|
| [`../AGENTS.md`](../AGENTS.md) | Original product / engineering build prompt |
| [`../DECISIONS.md`](../DECISIONS.md) | Dated architectural decisions |
| [`../OPERATOR_GUIDE.md`](../OPERATOR_GUIDE.md) | Install, env, CLI, retention notes |
| [`../SECURITY.md`](../SECURITY.md) | OWASP ASVS L2 checklist |
| [`../PROGRESS.md`](../PROGRESS.md) | Milestone gate history |
| [`../../README.md`](../../README.md) | Quick start |

## Conventions used here

- Paths are repo-relative (`src/…`, `web/…`).
- Defaults match `src/Channeled.Api/appsettings.json` unless noted.
- “Source of truth” for bytes = object store; for names/ACL/quotas = PostgreSQL.
- Authorization denials for hidden resources are **404**, not 403.
