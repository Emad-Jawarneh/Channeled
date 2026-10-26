# 06 — Chat, notifications & realtime

## Text channels

Channels with `Kind = Text` host messages rather than a file browser.

| Aspect | Design |
|--------|--------|
| Post / list / soft-delete | REST (`ChatEndpoints` / `ChatService`) |
| Body | Length-capped (≤4000); stored as text |
| Soft delete | Clears body; row retained until chat purge retention |
| Replies | Optional `ReplyToMessageId`; preview DTO on list |
| File chips | Message ↔ file refs (channel files the author can see) |
| Rate limit | `Chat:PostsPerMinutePerUser` (default 30) |
| Authz | Member+ to post (subject to channel write); own delete or Admin+ moderate |

List is cursor-paged (newest-first toward older). Missing authors render as **“Deleted user”** (user row gone; messages kept — see privacy note in operator guide / deletion doc).

## Mentions

- Resolved at post time (`MentionResolver`).
- Persisted in `message_mentions`.
- Creates inbox notifications (`NotificationType.Mention`) for mentioned users.
- After user delete: **mention notifications where the deleted user is actor are kept**; the deleted user’s own inbox is wiped.

## Notifications

Entity: `notifications` — types include Mention, Reply, TeamJoinRequest.

- REST list/read/unread helpers (`NotificationEndpoints`).
- Join-request notifications cleared when requests are approved/rejected/cancelled.
- Team delete removes all notifications referencing that `TeamId` (zero dangling rows).
- Realtime push can target the user via SignalR user groups (alongside REST poll/refresh).

## Realtime (SignalR)

Hub: `/hubs/channel` — **WebSockets only**.

| Direction | Methods / events |
|-----------|------------------|
| Client → server | `JoinChannel`, `LeaveChannel` only |
| Server → client | `filesChanged`, `messageCreated`, `messageDeleted`, `presenceChanged` (+ auth-state related pushes) |

Mutations always go through REST so Authorizer, audit, and rate limits stay in one path. The hub is fan-out after commit.

Coalescing: `CoalescingChannelNotifier` batches noisy `filesChanged` using `Realtime:CoalesceQuietMilliseconds` / `CoalesceMaxWaitMilliseconds`.

Caps / safety:

- `Realtime:MaxConnectionsPerUser`
- `Realtime:MaxGroupsPerConnection`
- `Realtime:MaxInboundMessageBytes`
- `Realtime:AllowedOrigins` (Origin allow-list)

Presence is channel-scoped and lightweight (online set for joined connections).

## Auth-state push

Hosted `AuthStateChangedService` (Api layer) helps clients learn about session invalidation / stamp changes without relying solely on the next HTTP 401. Related to session revoke and security-stamp validation (see [04 — Identity](04-identity-and-sessions.md)).

## Chat purge

`ChatPurgeHostedService` removes soft-deleted message rows older than `Chat:DeletedRetentionDays` on interval `Chat:PurgeIntervalHours`. This is separate from trash/file purge and from team delete (which hard-deletes messages in-txn).

## SPA chat client notes

- Feature area: `web/src/features/teams/TextChannelPage.tsx` + `chatState.ts`.
- Local reducer merges REST pages with hub events; resync buffer handles offline create/delete races.
- Mentions rendered as non-HTML text segments (no `dangerouslySetInnerHTML`).
