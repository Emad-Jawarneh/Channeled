# 10 — Frontend

## Stack

| Piece | Choice |
|-------|--------|
| Framework | React 19 + TypeScript (strict) |
| Bundler | Vite 7 |
| Routing | React Router 7 |
| Server state | TanStack Query |
| Realtime | `@microsoft/signalr` |
| UI | Radix primitives + CSS modules + design tokens |
| Tests | Vitest + Testing Library; Playwright e2e |

Package root: `web/`.

## Layout

```text
web/src/
├── api/           # HTTP client + generated/typed helpers (channeled.ts)
├── auth/          # AuthContext, cookie session bootstrap
├── components/    # AppShell, UserChip, shared chrome
├── features/      # Route-level feature modules
│   ├── admin/
│   ├── account/
│   ├── files/     # FileBrowser, upload, clipboard, locate
│   ├── teams/     # Channels, members, settings, text chat
│   ├── personal/
│   ├── search/
│   ├── login/ signup/ landing/ welcome/ …
├── realtime/      # Hub hooks (useChannelHub)
├── ui/            # Button and small primitives
└── lib/           # formatters, small utilities
```

## Auth client pattern

1. Login → session cookie set by API.
2. `GET /api/v1/auth/csrf` → store token for mutations.
3. All unsafe `fetch`/client calls attach `X-CSRF-TOKEN`.
4. `GET /api/v1/auth/me` hydrates `AuthContext`.
5. 401 / auth-state hub events → clear local user and redirect to login.

No tokens in `localStorage`. No `dangerouslySetInnerHTML`.

## Feature responsibilities

| Feature | Role |
|---------|------|
| `files/` | Infinite keyset listing, bulk bar, trash, preview, upload button, clipboard shelf, `/open/:id` locate |
| `personal/` | My files — same browser patterns, personal API |
| `teams/` | Team rail, channel sidebar, file vs text channel pages, members/roles, join, settings (incl. team delete confirm) |
| `search/` | Global name search + hit location routing |
| `admin/` | Platform admin surfaces; user delete dialog with transfer/delete resolutions |
| `account/` | Profile, security, sessions |

## Realtime usage

`useChannelHub` joins the active channel group. File browsers invalidate/refetch on `filesChanged` (coalesced server-side). Text channels merge `messageCreated` / `messageDeleted` / presence into a local reducer (`chatState.ts`) with a resync buffer for reconnect gaps.

## Authorization in the UI

The SPA **hides** controls the user likely cannot use (role from membership payload). It must never be the security boundary — every action re-authorizes on the server; failures surface as 404/400 with stable codes.

## Deleted-user presentation

When listing files after an uploader account is gone, API returns `uploadedByDisplayName: "Deleted user"` and `uploadedByStatus: "deleted"`. `UserChip` renders the deleted state (non-navigable). Chat authors missing from the users table similarly show “Deleted user”.

## API typing

Prefer the generated/typed client in `web/src/api/channeled.ts` (from OpenAPI). Keep DTOs aligned with Application records when extending endpoints.

## Design / mockups

Low-fidelity wireframes and HTML mockups live under `docs/design/` (historical UI exploration). Product visual language is owned by the SPA CSS modules/tokens — system design for **behavior** is this folder; pixel identity is not specified here.

Note: root `docs/design.md` is an **Improve UI audit skill prompt**, not the product architecture. Use `docs/system-design/` for as-built system design.
