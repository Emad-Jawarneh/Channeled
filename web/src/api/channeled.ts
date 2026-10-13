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

export type ItemList = { items: FileItem[]; nextCursor?: string | null };

export type TeamMember = {
  userId: string;
  email: string;
  displayName: string;
  role: number;
  joinedAt: string;
};

export type ChannelJoinInfo = {
  teamSlug: string;
  channelName: string;
  visibility: number;
  hasAccess: boolean;
  pendingRequestId?: string | null;
  joinPath: string;
};

export type ChannelJoinRequest = {
  id: string;
  userId: string;
  email: string;
  displayName: string;
  status: number;
  createdAt: string;
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
export const ChannelVisibility = { Open: 0, Private: 1 } as const;
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
  invite: (slug: string, email: string, role: number) =>
    api<{ id: string; email: string; role: number; expiresAt: string; rawToken?: string | null }>(
      `/api/v1/teams/${slug}/invitations`,
      { method: "POST", json: { email, role } },
    ),
  acceptTeamInvite: (token: string) =>
    api<Team>("/api/v1/teams/invitations/accept", { method: "POST", json: { token } }),
  upsertChannelMember: (slug: string, channel: string, userId: string, canWrite: boolean, canManage: boolean) =>
    api<void>(`/api/v1/teams/${slug}/channels/${channel}/members`, {
      method: "PUT",
      json: { userId, canWrite, canManage },
    }),
  channelJoinInfo: (slug: string, channel: string) =>
    api<ChannelJoinInfo>(`/api/v1/teams/${slug}/channels/${channel}/join`),
  requestChannelJoin: (slug: string, channel: string) =>
    api<ChannelJoinRequest>(`/api/v1/teams/${slug}/channels/${channel}/join-requests`, { method: "POST" }),
  listChannelJoinRequests: (slug: string, channel: string) =>
    api<ChannelJoinRequest[]>(`/api/v1/teams/${slug}/channels/${channel}/join-requests`),
  approveChannelJoin: (slug: string, channel: string, requestId: string) =>
    api<void>(`/api/v1/teams/${slug}/channels/${channel}/join-requests/${requestId}/approve`, {
      method: "POST",
      json: {},
    }),
  rejectChannelJoin: (slug: string, channel: string, requestId: string) =>
    api<void>(`/api/v1/teams/${slug}/channels/${channel}/join-requests/${requestId}/reject`, {
      method: "POST",
    }),
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

export const NotificationType = { Mention: 1, Reply: 2 } as const;

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
  list: (opts: { limit?: number; unreadOnly?: boolean } = {}) => {
    const q = new URLSearchParams();
    q.set("limit", String(opts.limit ?? 20));
    if (opts.unreadOnly) q.set("unreadOnly", "true");
    return api<NotificationDto[]>(`/api/v1/notifications?${q}`);
  },
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
    const response = await fetch(`/api/v1/uploads/${uploadId}/parts/${partNumber}`, {
      method: "PUT",
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/octet-stream",
        "X-CSRF-TOKEN": csrf,
      },
      body: blob,
    });
    if (!response.ok) {
      throw new Error(`Part upload failed (${response.status})`);
    }
    return response.json() as Promise<{ partNumber: number; eTag: string; sizeBytes: number }>;
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

export const searchApi = {
  search: (q: string, scope: string, teamSlug?: string, channelName?: string) => {
    const params = new URLSearchParams({ q, scope });
    if (teamSlug) params.set("teamSlug", teamSlug);
    if (channelName) params.set("channelName", channelName);
    return api<{ items: FileItem[] }>(`/api/v1/search?${params}`);
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
