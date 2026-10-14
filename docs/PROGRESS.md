# Channeled Progress

## Milestones

| Milestone | Status | Evidence |
|-----------|--------|----------|
| M0–M7 | ✅ Complete | Prior gates |
| M8 Hardening | ✅ Complete | Playwright e2e + axe; `docs/SECURITY.md`; load-test script; `./ci.sh` green |
| M9 Realtime | ✅ Complete | SignalR WS-only hub; Origin filter; coalesce; AuthStateChanged; hub Join/Leave only; API hub tests |
| M10 Public links | ✅ Complete | ShareLinkApiTests (claim/idempotency/expiry/purge FK/move/anon vs auth); unit primitives; restore revokes links |
| M11 Text chat | ✅ Complete | ChatApiTests + ChatHubTests + ChatAuthorizerTests + rate-limit; TextChannelPage composer |
| M12 Replies / mentions / notifications | ✅ Complete | ReplyMentionNotificationTests + MentionResolverTests; NotificationBell + TextChannelPage reply/@ |
| M13 Channel edit / delete | ✅ Complete | ChannelDeleteApiTests (discard/move/quota/perms); settings edit + type-to-confirm delete (move|discard) |
| M14 Account / Profiles (no 2FA) | ✅ Complete | Usernames, avatars, UserChip, account settings, SMTP + self-service mail, fragment confirm/reset, session revoke on password/email change |
| M15 Chat file refs (Phase 3) | ✅ Complete | `message_file_refs` + resolve API; MessageFileRefsApiTests; TextChannelPage attach/# + FileBrowser “Share to chat…” |
| M16 Motion tokens | ✅ Complete | `--dur-*` / reduced-motion; skeletons; Playwright reducedMotion; bundle budget script |
| M17 File clipboard | ✅ Complete | `POST /api/v1/clipboard/paste`; client chip + shortcuts; ClipboardPasteApiTests + e2e smoke |

## M9–M12 notes

- Hub: `/hubs/channel`, WebSockets only; client methods `JoinChannel` / `LeaveChannel` only; auto-join `user:{id}` for notifications
- SPA: `@microsoft/signalr`; Vite proxies `/hubs` with `ws: true`
- Public links: `PublicFileLinks:*` (default Enabled=false); anonymous `/api/v1/public/shares/{token}` + `/s/:token` UI
- Chat: REST under channel routes; presence in-memory (text only); message purge after retention
- Replies: `ReplyToMessageId`; mentions: `message_mentions` + `@DisplayName`; notifications: Mention/Reply + `/api/v1/notifications`
- Stack minimum: PostgreSQL 15+ (documented in AGENTS + OPERATOR_GUIDE)

## M8 exit gate

- [x] Playwright e2e: sign in; upload; rename; move; delete; restore; invite/accept; role change; private channel; personal copy; admin audit
- [x] Accessibility (axe) pass on channel browser
- [x] `docs/SECURITY.md` ASVS L2 filled
- [x] Load test script (CI reduced profile; full 200/50/1M documented)
- [x] README quickstart; `./ci.sh` green

## Open risks

- Claim idempotency and ticket byte counters are in-memory (restart races documented in DECISIONS)
- Presence/hub registry are single-process (no Redis backplane)
- OpenAPI/`generated.ts` not fully covering new share/chat routes (hand-written `channeled.ts` used)
