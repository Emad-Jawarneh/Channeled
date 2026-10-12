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
  createChannel: (slug: string, name: string, visibility: number, description?: string) =>
    api<Channel>(`/api/v1/teams/${slug}/channels`, {
      method: "POST",
      json: { name, description: description ?? null, visibility },
    }),
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
  move: (slug: string, channel: string, id: string, targetFolderId: string | null) =>
    api<FileItem>(`/api/v1/teams/${slug}/channels/${channel}/items/${id}/move`, {
      method: "POST",
      json: { targetFolderId },
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
  rename: (id: string, name: string) =>
    api<FileItem>(`/api/v1/me/files/items/${id}`, { method: "PATCH", json: { name } }),
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
