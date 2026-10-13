import { api } from "./http";

export type AuthUser = {
  id: string;
  email: string;
  displayName: string;
  isPlatformAdmin: boolean;
};

export type Team = {
  id: string;
  name: string;
  slug: string;
  description?: string | null;
  quotaBytes: number;
  archivedAt?: string | null;
  myRole?: number | null;
  /** TeamJoinMode: 0 OpenJoin, 1 RequestToJoin. */
  joinMode: number;
  /** MemberRole applied to joins/approvals by default (Viewer or Member). */
  defaultJoinRole: number;
};

export type Channel = {
  id: string;
  teamId: string;
  name: string;
  description?: string | null;
  visibility: number;
  kind: number;
  archivedAt?: string | null;
};

export type FileItem = {
  id: string;
  kind: number;
  name: string;
  folderId?: string | null;
  channelId?: string | null;
  contentType?: string | null;
  sizeBytes?: number | null;
  sha256?: string | null;
  uploadedBy?: string | null;
  createdAt: string;
  updatedAt: string;
  deletedAt?: string | null;
  ownerUserId?: string | null;
  starred?: boolean;
};

export type FolderPathSegment = { id: string; name: string };

export type ItemList = {
  items: FileItem[];
  nextCursor?: string | null;
  path?: FolderPathSegment[] | null;
};

export type TeamMember = {
  userId: string;
  email: string;
  displayName: string;
  role: number;
  joinedAt: string;
};

export type TeamJoinRequest = {
  id: string;
  userId: string;
  email: string;
  displayName: string;
  status: number;
  createdAt: string;
};

export type TeamJoinLink = {
  token: string;
  prefix: string;
  expiresAt?: string | null;
  /** Server-relative path, e.g. `/join/<token>`. */
  joinPath: string;
};

export type TeamJoinInfo = {
  teamName: string;
  joinMode: number;
  isMember: boolean;
  pendingRequestId?: string | null;
};

export type TeamJoinResult = {
  teamSlug: string;
  role?: number | null;
  pendingRequestId?: string | null;
  joined: boolean;
};

export type UpdateTeamSettingsInput = {
  name?: string;
  description?: string | null;
  joinMode?: number;
  defaultJoinRole?: number;
  confirmOpenJoinApprovePending?: boolean;
};

export type UpdateTeamSettingsResult = { team: Team; approvedCount?: number | null };

export type SetChannelVisibilityResult = {
  channel: Channel;
  membersLosingAccess: number;
  shareLinksRevoked: number;
};

export type ChannelMember = {
  userId: string;
  email: string;
  displayName: string;
  readOnly: boolean;
};

export type AuditEvent = {
  id: string;
  occurredAt: string;
  actorId?: string | null;
  eventType: number;
  teamId?: string | null;
  targetType: string;
  targetId: string;
  detailsJson: string;
  ip: string;
  userAgent: string;
};

export const ItemKind = { File: 0, Folder: 1 } as const;
export const MemberRole = { Viewer: 0, Member: 1, Admin: 2, Owner: 3 } as const;
export const ChannelVisibility = { Everyone: 0, Restricted: 1 } as const;
export const TeamJoinMode = { OpenJoin: 0, RequestToJoin: 1 } as const;
export const ChannelKind = { File: 0, Text: 1 } as const;

export const authApi = {
  me: () => api<AuthUser>("/api/v1/auth/me"),
  login: (email: string, password: string) =>
    api<AuthUser>("/api/v1/auth/login", { method: "POST", json: { email, password } }),
  logout: () => api<void>("/api/v1/auth/logout", { method: "POST" }),
  register: (email: string, displayName: string, password: string) =>
    api<AuthUser>("/api/v1/auth/register", {
      method: "POST",
      json: { email, displayName, password },
    }),
  acceptInvite: (token: string, displayName: string, password: string) =>
    api<AuthUser>("/api/v1/auth/invitations/accept", {
      method: "POST",
      json: { token, displayName, password },
    }),
  resetPassword: (token: string, newPassword: string) =>
    api<void>("/api/v1/auth/reset-password", {
      method: "POST",
      json: { token, newPassword },
    }),
};

export const teamsApi = {
  list: () => api<Team[]>("/api/v1/teams/"),
  create: (name: string, description?: string) =>
    api<Team>("/api/v1/teams/", { method: "POST", json: { name, description: description ?? null } }),
  get: (slug: string) => api<Team>(`/api/v1/teams/${slug}`),
  channels: (slug: string) => api<Channel[]>(`/api/v1/teams/${slug}/channels`),
  createChannel: (
    slug: string,
    input: {
      name: string;
      visibility: number;
      kind: number;
      description?: string | null;
    },
  ) =>
    api<Channel>(`/api/v1/teams/${slug}/channels`, {
      method: "POST",
      json: {
        name: input.name,
        description: input.description ?? null,
        visibility: input.visibility,
        kind: input.kind,
      },
    }),
  updateChannel: (
    slug: string,
    channel: string,
    input: { name?: string; description?: string | null },
  ) =>
    api<Channel>(`/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}`, {
      method: "PUT",
      json: {
        name: input.name ?? null,
        description: input.description === undefined ? null : input.description,
      },
    }),
  archiveChannel: (slug: string, channel: string) =>
    api<void>(`/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/archive`, {
      method: "POST",
    }),
  deleteChannel: (
    slug: string,
    channel: string,
    input?: { discardFiles?: boolean; moveFilesToChannel?: string | null },
  ) =>
    api<void>(
      `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/delete`,
      {
        method: "POST",
        json: {
          discardFiles: input?.discardFiles ?? null,
          moveFilesToChannel: input?.moveFilesToChannel ?? null,
        },
      },
    ),
  members: (slug: string) => api<TeamMember[]>(`/api/v1/teams/${slug}/members`),
  changeRole: (slug: string, userId: string, role: number) =>
    api<void>(`/api/v1/teams/${slug}/members/${userId}/role`, {
      method: "PUT",
      json: { role },
    }),
  removeMember: (slug: string, userId: string) =>
    api<void>(`/api/v1/teams/${encodeURIComponent(slug)}/members/${userId}`, { method: "DELETE" }),
  leave: (slug: string) =>
    api<void>(`/api/v1/teams/${encodeURIComponent(slug)}/leave`, { method: "POST" }),

  updateSettings: (slug: string, input: UpdateTeamSettingsInput) =>
    api<UpdateTeamSettingsResult>(`/api/v1/teams/${encodeURIComponent(slug)}/settings`, {
      method: "PUT",
      json: {
        name: input.name ?? null,
        description: input.description ?? null,
        joinMode: input.joinMode ?? null,
        defaultJoinRole: input.defaultJoinRole ?? null,
        confirmOpenJoinApprovePending: input.confirmOpenJoinApprovePending ?? false,
      },
    }),

  // ---- Join link (Admin+) ----
  /** 409 `join_link_unavailable` when no link exists yet: call `ensureJoinLink`. */
  getJoinLink: (slug: string) =>
    api<TeamJoinLink>(`/api/v1/teams/${encodeURIComponent(slug)}/join-link`),
  ensureJoinLink: (slug: string) =>
    api<TeamJoinLink>(`/api/v1/teams/${encodeURIComponent(slug)}/join-link`, { method: "POST" }),
  rotateJoinLink: (slug: string, expiresAt?: string | null) =>
    api<TeamJoinLink>(`/api/v1/teams/${encodeURIComponent(slug)}/join-link/rotate`, {
      method: "POST",
      json: { expiresAt: expiresAt ?? null },
    }),

  // ---- Join by token ----
  getTeamJoinInfo: (token: string) =>
    api<TeamJoinInfo>(`/api/v1/teams/join/${encodeURIComponent(token)}`),
  joinTeam: (token: string) =>
    api<TeamJoinResult>(`/api/v1/teams/join/${encodeURIComponent(token)}`, { method: "POST" }),

  // ---- Join requests ----
  listJoinRequests: (slug: string) =>
    api<TeamJoinRequest[]>(`/api/v1/teams/${encodeURIComponent(slug)}/join-requests`),
  approveJoinRequest: (slug: string, requestId: string, role: number) =>
    api<void>(`/api/v1/teams/${encodeURIComponent(slug)}/join-requests/${requestId}/approve`, {
      method: "POST",
      json: { role },
    }),
  rejectJoinRequest: (slug: string, requestId: string) =>
    api<void>(`/api/v1/teams/${encodeURIComponent(slug)}/join-requests/${requestId}/reject`, {
      method: "POST",
    }),
  withdrawJoinRequest: (requestId: string) =>
    api<void>(`/api/v1/teams/join-requests/${requestId}/withdraw`, { method: "POST" }),
  pendingJoinCount: () => api<{ count: number }>("/api/v1/notifications/pending-join-count"),

  // ---- Channel visibility + grants ----
  setChannelVisibility: (slug: string, channel: string, input: { visibility: number }) =>
    api<SetChannelVisibilityResult>(
      `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/visibility`,
      {
        method: "PUT",
        json: { visibility: input.visibility },
      },
    ),
  channelMembers: (slug: string, channel: string) =>
    api<ChannelMember[]>(
      `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/members`,
    ),
  upsertChannelMember: (slug: string, channel: string, userId: string, readOnly: boolean) =>
    api<void>(
      `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/members`,
      { method: "PUT", json: { userId, readOnly } },
    ),
  bulkUpsertChannelMembers: (
    slug: string,
    channel: string,
    members: { userId: string; readOnly: boolean }[],
  ) =>
    api<void>(
      `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/members/bulk`,
      { method: "PUT", json: { members } },
    ),
  removeChannelMember: (slug: string, channel: string, userId: string) =>
    api<void>(
      `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/members/${userId}`,
      { method: "DELETE" },
    ),
};

export type ChannelMessage = {
  id: string;
  channelId: string;
  authorId: string;
  authorDisplayName: string;
  body: string;
  createdAt: string;
  /** Quote of the message this one replies to (absent/null when it is not a reply). */
  replyTo?: ReplyPreview | null;
  mentions: MessageMention[];
};

export type ReplyPreview = {
  id: string;
  authorId: string;
  authorDisplayName: string;
  bodyPreview: string;
  deleted: boolean;
};

export type MessageMention = { userId: string; displayName: string };

export type PostMessageRequest = {
  body: string;
  replyToMessageId?: string | null;
  mentionedUserIds?: string[];
};

/** Newest-first page; `nextCursor` pages toward older messages. */
export type MessageList = { items: ChannelMessage[]; nextCursor?: string | null };

export type PresenceUser = { userId: string; displayName: string };
export type PresenceList = { channelId: string; users: PresenceUser[] };

/** Hub `messageDeleted` payload. */
export type MessageDeletedEvent = { channelId: string; messageId: string };

/** Hub `presenceChanged` payload. */
export type PresenceChangedEvent = {
  channelId: string;
  userId: string;
  displayName: string;
  online: boolean;
};

export const MESSAGE_MAX_LENGTH = 4000;
export const MESSAGE_PAGE_SIZE = 50;

/** Text-channel chat. Posts and deletes are REST only; the hub just pushes the committed result. */
export const messagesApi = {
  list: (slug: string, channel: string, opts: { cursor?: string | null; limit?: number } = {}) => {
    const q = new URLSearchParams();
    q.set("limit", String(opts.limit ?? MESSAGE_PAGE_SIZE));
    if (opts.cursor) q.set("cursor", opts.cursor);
    return api<MessageList>(
      `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/messages?${q}`,
    );
  },
  post: (slug: string, channel: string, request: PostMessageRequest) =>
    api<ChannelMessage>(
      `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/messages`,
      {
        method: "POST",
        json: {
          body: request.body,
          replyToMessageId: request.replyToMessageId ?? null,
          mentionedUserIds: request.mentionedUserIds ?? [],
        },
      },
    ),
  remove: (slug: string, channel: string, messageId: string) =>
    api<void>(
      `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/messages/${messageId}`,
      { method: "DELETE" },
    ),
  presence: (slug: string, channel: string) =>
    api<PresenceList>(
      `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/presence`,
    ),
};

export const NotificationType = { Mention: 1, Reply: 2, TeamJoinRequest: 3 } as const;

export type NotificationDto = {
  id: string;
  type: number;
  teamId: string;
  teamSlug: string;
  channelId: string;
  channelName: string;
  messageId: string;
  actorId: string;
  actorDisplayName: string;
  bodyPreview: string;
  createdAt: string;
  readAt?: string | null;
};

export const notificationsApi = {
  /** Server returns `{ items, unreadCount }`; normalised to the item array here. */
  list: async (opts: { limit?: number; unreadOnly?: boolean } = {}): Promise<NotificationDto[]> => {
    const q = new URLSearchParams();
    q.set("limit", String(opts.limit ?? 20));
    if (opts.unreadOnly) q.set("unreadOnly", "true");
    const res = await api<NotificationDto[] | { items: NotificationDto[]; unreadCount?: number }>(
      `/api/v1/notifications?${q}`,
    );
    return Array.isArray(res) ? res : (res?.items ?? []);
  },
  pendingJoinCount: () => api<{ count: number }>("/api/v1/notifications/pending-join-count"),
  unreadCount: () => api<{ unreadCount: number }>("/api/v1/notifications/unread-count"),
  markRead: (id: string) =>
    api<void>(`/api/v1/notifications/${encodeURIComponent(id)}/read`, { method: "POST" }),
  markAllRead: () => api<void>("/api/v1/notifications/read-all", { method: "POST" }),
};

export type ShareLinkCreated = {
  id: string;
  token: string;
  path: string;
  fileId?: string | null;
  fileName: string;
  createdAt: string;
  expiresAt: string;
  maxDownloads?: number | null;
};

export type ShareLink = {
  id: string;
  fileId?: string | null;
  fileName: string;
  createdBy: string;
  createdAt: string;
  expiresAt: string;
  maxDownloads?: number | null;
  downloadCount: number;
  revokedAt?: string | null;
  revokeReason?: string | null;
  status: "active" | "revoked" | "expired" | "exhausted";
};

export type PublicShareInfo = {
  fileName: string;
  sizeBytes: number;
  contentType: string;
  expiresAt: string;
  remainingDownloads?: number | null;
};

export type ShareClaim = {
  ticket: string;
  ticketExpiresAt: string;
  downloadPath: string;
  fileName: string;
  sizeBytes: number;
};

/** Authenticated share-link management. The raw token is only ever returned by `create`. */
export const shareLinksApi = {
  create: (slug: string, channel: string, fileId: string, body: { expiresInDays?: number; maxDownloads?: number }) =>
    api<ShareLinkCreated>(`/api/v1/teams/${slug}/channels/${channel}/items/${fileId}/share-links`, {
      method: "POST",
      json: body,
    }),
  list: (slug: string, channel: string, fileId?: string) =>
    api<ShareLink[]>(
      `/api/v1/teams/${slug}/channels/${channel}/share-links${fileId ? `?fileId=${encodeURIComponent(fileId)}` : ""}`,
    ),
  revoke: (slug: string, linkId: string) =>
    api<void>(`/api/v1/teams/${slug}/share-links/${linkId}`, { method: "DELETE" }),
};

/** Anonymous endpoints used by the /s/:token page (no session, no CSRF). */
export const publicShareApi = {
  info: (token: string) => api<PublicShareInfo>(`/api/v1/public/shares/${encodeURIComponent(token)}`),
  claim: (token: string, idempotencyKey: string) =>
    api<ShareClaim>(`/api/v1/public/shares/${encodeURIComponent(token)}/claim`, {
      method: "POST",
      headers: { "Idempotency-Key": idempotencyKey },
    }),
};

export const filesApi = {
  list: (slug: string, channel: string, opts: { folderId?: string; trash?: boolean; sort?: string } = {}) => {
    const q = new URLSearchParams();
    if (opts.folderId) q.set("folderId", opts.folderId);
    if (opts.trash) q.set("trash", "true");
    if (opts.sort) q.set("sort", opts.sort);
    const qs = q.toString();
    return api<ItemList>(`/api/v1/teams/${slug}/channels/${channel}/items${qs ? `?${qs}` : ""}`);
  },
  contentUrl: (slug: string, channel: string, id: string, opts?: { download?: boolean }) => {
    const base = `/api/v1/teams/${encodeURIComponent(slug)}/channels/${encodeURIComponent(channel)}/items/${encodeURIComponent(id)}/content`;
    // ASP.NET Core bool query binding rejects "1"; must be "true"/"false".
    return opts?.download ? `${base}?download=true` : base;
  },
  rename: (slug: string, channel: string, id: string, name: string) =>
    api<FileItem>(`/api/v1/teams/${slug}/channels/${channel}/items/${id}`, {
      method: "PATCH",
      json: { name },
    }),
  move: (
    slug: string,
    channel: string,
    id: string,
    targetFolderId: string | null,
    targetChannelName?: string | null,
  ) =>
    api<FileItem>(`/api/v1/teams/${slug}/channels/${channel}/items/${id}/move`, {
      method: "POST",
      json: {
        targetFolderId,
        targetChannelName: targetChannelName ?? null,
      },
    }),
  trash: (slug: string, channel: string, id: string) =>
    api<void>(`/api/v1/teams/${slug}/channels/${channel}/items/${id}`, { method: "DELETE" }),
  restore: (slug: string, channel: string, id: string) =>
    api<void>(`/api/v1/teams/${slug}/channels/${channel}/items/${id}/restore`, { method: "POST" }),
  createFolder: (slug: string, channel: string, name: string, parentFolderId?: string | null) =>
    api<FileItem>(`/api/v1/teams/${slug}/channels/${channel}/folders`, {
      method: "POST",
      json: { name, parentFolderId: parentFolderId ?? null },
    }),
  startUpload: (body: {
    teamSlug: string;
    channelName: string;
    fileName: string;
    declaredSizeBytes: number;
    contentType?: string;
    folderId?: string | null;
  }) =>
    api<{ uploadId: string; multipartId: string; partSizeBytes: number; expiresAt: string }>(
      "/api/v1/uploads/",
      { method: "POST", json: body },
    ),
  putPart: async (uploadId: string, partNumber: number, blob: Blob, csrf: string) => {
    const maxAttempts = 5;
    let lastError: unknown;
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      try {
        const response = await fetch(`/api/v1/uploads/${uploadId}/parts/${partNumber}`, {
          method: "PUT",
          credentials: "same-origin",
          headers: {
            "Content-Type": "application/octet-stream",
            "X-CSRF-TOKEN": csrf,
          },
          body: blob,
        });
        if (response.ok) {
          return response.json() as Promise<{ partNumber: number; eTag: string; sizeBytes: number }>;
        }
        const err = new Error(`Part upload failed (${response.status})`);
        // Retry transient gateway / server errors; other 4xx are permanent.
        if (response.status < 500 && response.status !== 408 && response.status !== 429) {
          throw err;
        }
        lastError = err;
      } catch (err) {
        lastError = err;
        const msg = err instanceof Error ? err.message : "";
        const retryable =
          err instanceof TypeError ||
          /failed to fetch|networkerror|load failed|Part upload failed \(5\d\d\)|Part upload failed \(408\)|Part upload failed \(429\)/i.test(
            msg,
          );
        if (!retryable) {
          throw err;
        }
      }
      if (attempt < maxAttempts) {
        await new Promise((r) => setTimeout(r, Math.min(8_000, 400 * 2 ** (attempt - 1))));
      }
    }
    throw lastError instanceof Error ? lastError : new Error("Part upload failed");
  },
  complete: (uploadId: string, expectedSha256Hex?: string) =>
    api<FileItem>(`/api/v1/uploads/${uploadId}/complete`, {
      method: "POST",
      json: { expectedSha256Hex: expectedSha256Hex ?? null, idempotencyKey: null },
    }),
};

export const personalApi = {
  list: (opts: { folderId?: string; trash?: boolean } = {}) => {
    const q = new URLSearchParams();
    if (opts.folderId) q.set("folderId", opts.folderId);
    if (opts.trash) q.set("trash", "true");
    const qs = q.toString();
    return api<ItemList>(`/api/v1/me/files/items${qs ? `?${qs}` : ""}`);
  },
  contentUrl: (id: string, opts?: { download?: boolean }) => {
    const base = `/api/v1/me/files/items/${encodeURIComponent(id)}/content`;
    // ASP.NET Core bool query binding rejects "1"; must be "true"/"false".
    return opts?.download ? `${base}?download=true` : base;
  },
  recent: () => api<ItemList>("/api/v1/me/files/recent"),
  starred: () => api<ItemList>("/api/v1/me/files/starred"),
  createFolder: (name: string, parentFolderId?: string | null) =>
    api<FileItem>(`/api/v1/me/files/folders`, {
      method: "POST",
      json: { name, parentFolderId: parentFolderId ?? null },
    }),
  rename: (id: string, name: string) =>
    api<FileItem>(`/api/v1/me/files/items/${id}`, { method: "PATCH", json: { name } }),
  move: (id: string, targetFolderId: string | null) =>
    api<FileItem>(`/api/v1/me/files/items/${id}/move`, {
      method: "POST",
      json: { targetFolderId },
    }),
  trash: (id: string) => api<void>(`/api/v1/me/files/items/${id}`, { method: "DELETE" }),
  restore: (id: string) => api<void>(`/api/v1/me/files/items/${id}/restore`, { method: "POST" }),
  copyToChannel: (id: string, teamSlug: string, channelName: string, newName?: string) =>
    api<FileItem>(`/api/v1/me/files/items/${id}/copy-to-channel`, {
      method: "POST",
      json: {
        teamSlug,
        channelName,
        targetFolderId: null,
        newName: newName ?? null,
      },
    }),
  startUpload: (body: {
    fileName: string;
    declaredSizeBytes: number;
    contentType?: string;
    folderId?: string | null;
  }) =>
    api<{ uploadId: string; partSizeBytes: number }>(`/api/v1/me/files/uploads`, {
      method: "POST",
      json: body,
    }),
};

/** Domain enum values. The query string accepts the lowercase name (`accessible`) or the number. */
export const SearchScope = { Channel: 0, Team: 1, Personal: 2, Accessible: 3 } as const;
const SEARCH_SCOPE_NAMES = ["channel", "team", "personal", "accessible"] as const;

export type SearchHit = {
  id: string;
  kind: number;
  name: string;
  channelId?: string | null;
  ownerUserId?: string | null;
  contentType?: string | null;
  sizeBytes?: number | null;
  uploadedBy?: string | null;
  updatedAt: string;
  channelName?: string | null;
  teamSlug?: string | null;
};

export type SearchResponse = { hits: SearchHit[]; nextCursor?: string | null };

export const searchApi = {
  search: (
    q: string,
    scope: number | string = SearchScope.Accessible,
    opts: { teamSlug?: string; channelName?: string; cursor?: string | null; limit?: number } = {},
  ) => {
    const scopeName = typeof scope === "number" ? SEARCH_SCOPE_NAMES[scope] : scope;
    const params = new URLSearchParams({ q, scope: scopeName });
    if (opts.teamSlug) params.set("teamSlug", opts.teamSlug);
    if (opts.channelName) params.set("channelName", opts.channelName);
    if (opts.cursor) params.set("cursor", opts.cursor);
    if (opts.limit) params.set("limit", String(opts.limit));
    return api<SearchResponse>(`/api/v1/search?${params}`);
  },
};

export const adminApi = {
  dashboard: () =>
    api<{
      userCount: number;
      activeUserCount: number;
      teamCount: number;
      channelCount: number;
      fileCount: number;
      totalFileBytes: number;
      auditEventsLast24h: number;
    }>("/api/v1/admin/dashboard"),
  health: () => api<{ status: string; database: boolean; store: boolean }>("/api/v1/admin/health"),
  users: () =>
    api<
      {
        id: string;
        email: string;
        displayName: string;
        status: string;
        isPlatformAdmin: boolean;
      }[]
    >("/api/v1/admin/users"),
  teams: () =>
    api<{ id: string; name: string; slug: string; quotaBytes: number; usageBytes: number }[]>(
      "/api/v1/admin/teams",
    ),
  audit: (limit = 50) =>
    api<{ items: AuditEvent[]; nextCursor?: string | null }>(`/api/v1/admin/audit?limit=${limit}`),
};
