import AxeBuilder from "@axe-core/playwright";
import {
  test as base,
  expect,
  request as playwrightRequest,
  type APIRequestContext,
  type APIResponse,
  type BrowserContext,
  type Page,
} from "@playwright/test";

export { expect };

export const BASE_URL = process.env.PLAYWRIGHT_BASE_URL ?? "http://127.0.0.1:5173";
export const E2E_ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? "e2e-admin@example.com";
export const E2E_ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? "E2eAdminPassword1!";
const PASSWORD = "E2ePassword1!xyz";

export const Role = { Viewer: 0, Member: 1, Admin: 2, Owner: 3 } as const;
export const Visibility = { Everyone: 0, Restricted: 1 } as const;
export const Kind = { File: 0, Text: 1 } as const;
export type Theme = "light" | "dark";

/** A signed-in user driven through the API (cookie session + CSRF token). */
export type Person = {
  id: string;
  email: string;
  password: string;
  displayName: string;
  username: string;
  api: APIRequestContext;
  csrf: string;
};

export type TeamRef = { id: string; slug: string; name: string };
export type ItemRef = { id: string; name: string; kind: number; folderId?: string | null };

let sequence = 0;

/** Short unique suffix so parallel workers and repeated runs never collide on names. */
export function unique(): string {
  sequence += 1;
  return `${Date.now().toString(36)}${sequence.toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

async function ok(response: APIResponse, what: string): Promise<APIResponse> {
  if (!response.ok()) {
    throw new Error(`${what} failed: ${response.status()} ${await response.text()}`);
  }
  return response;
}

async function json<T>(response: APIResponse, what: string): Promise<T> {
  await ok(response, what);
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

async function finishSignIn(api: APIRequestContext, email: string, password: string): Promise<Person> {
  const csrf = await json<{ token: string }>(await api.get("/api/v1/auth/csrf"), "csrf");
  const me = await json<{ id: string; displayName: string; username: string }>(
    await api.get("/api/v1/auth/me"),
    "me",
  );
  return { id: me.id, email, password, displayName: me.displayName, username: me.username, api, csrf: csrf.token };
}

export async function signUp(label = "User", displayName?: string): Promise<Person> {
  const api = await playwrightRequest.newContext({ baseURL: BASE_URL });
  const suffix = unique();
  const email = `e2e-${label.toLowerCase().replace(/[^a-z0-9]+/g, "-")}-${suffix}@example.com`;
  const name = displayName ?? `${label} ${suffix.slice(-5)}`;
  await ok(
    await api.post("/api/v1/auth/register", { data: { email, displayName: name, password: PASSWORD } }),
    `register ${email}`,
  );
  return finishSignIn(api, email, PASSWORD);
}

export async function signIn(email: string, password: string): Promise<Person> {
  const api = await playwrightRequest.newContext({ baseURL: BASE_URL });
  await ok(await api.post("/api/v1/auth/login", { data: { email, password } }), `login ${email}`);
  return finishSignIn(api, email, password);
}

export function signInAdmin(): Promise<Person> {
  return signIn(E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
}

function headers(p: Person, extra?: Record<string, string>): Record<string, string> {
  return { "X-CSRF-TOKEN": p.csrf, ...extra };
}

export async function call<T>(p: Person, method: "GET" | "POST" | "PUT" | "PATCH" | "DELETE", path: string, data?: unknown): Promise<T> {
  const response = await p.api.fetch(path, {
    method,
    headers: method === "GET" ? undefined : headers(p),
    data: data === undefined ? undefined : data,
  });
  return json<T>(response, `${method} ${path}`);
}

export async function createTeam(owner: Person, name = `Team ${unique()}`): Promise<TeamRef> {
  const team = await call<TeamRef>(owner, "POST", "/api/v1/teams/", { name, description: null });
  await createChannel(owner, team.slug, "general");
  return team;
}

export async function createChannel(
  owner: Person,
  teamSlug: string,
  name: string,
  opts: { kind?: number; visibility?: number; description?: string | null } = {},
): Promise<{ id: string; name: string }> {
  return call(owner, "POST", `/api/v1/teams/${teamSlug}/channels`, {
    name,
    description: opts.description ?? null,
    visibility: opts.visibility ?? Visibility.Everyone,
    kind: opts.kind ?? Kind.File,
  });
}

/** Adds `member` to the team through the join link (Open join), then promotes if needed. */
export async function addMember(owner: Person, teamSlug: string, member: Person, role: number = Role.Member) {
  await call(owner, "PUT", `/api/v1/teams/${teamSlug}/settings`, {
    name: null,
    description: null,
    joinMode: 0,
    defaultJoinRole: role === Role.Viewer ? Role.Viewer : Role.Member,
    confirmOpenJoinApprovePending: true,
  });
  const link = await call<{ joinPath: string }>(owner, "POST", `/api/v1/teams/${teamSlug}/join-link`);
  const token = link.joinPath.split("/join/")[1];
  expect(token, "join token").toBeTruthy();
  await call(member, "POST", `/api/v1/teams/join/${encodeURIComponent(token!)}`);
  if (role === Role.Admin) {
    await call(owner, "PUT", `/api/v1/teams/${teamSlug}/members/${member.id}/role`, { role: Role.Admin });
  }
}

export async function grantChannel(owner: Person, teamSlug: string, channel: string, userId: string, readOnly = false) {
  await call(owner, "PUT", `/api/v1/teams/${teamSlug}/channels/${channel}/members`, { userId, readOnly });
}

export async function revokeChannel(owner: Person, teamSlug: string, channel: string, userId: string) {
  await call(owner, "DELETE", `/api/v1/teams/${teamSlug}/channels/${channel}/members/${userId}`);
}

export async function createFolder(
  p: Person,
  teamSlug: string,
  channel: string,
  name: string,
  parentFolderId: string | null = null,
): Promise<ItemRef> {
  return call(p, "POST", `/api/v1/teams/${teamSlug}/channels/${channel}/folders`, { name, parentFolderId });
}

export async function createPersonalFolder(p: Person, name: string, parentFolderId: string | null = null): Promise<ItemRef> {
  return call(p, "POST", "/api/v1/me/files/folders", { name, parentFolderId });
}

async function sendParts(p: Person, uploadId: string, partSize: number, bytes: Buffer): Promise<ItemRef> {
  const parts = Math.max(1, Math.ceil(bytes.length / partSize));
  for (let part = 1; part <= parts; part++) {
    const slice = bytes.subarray((part - 1) * partSize, Math.min(part * partSize, bytes.length));
    await ok(
      await p.api.put(`/api/v1/uploads/${uploadId}/parts/${part}`, {
        headers: headers(p, { "Content-Type": "application/octet-stream" }),
        data: slice,
      }),
      `part ${part}`,
    );
  }
  return call<ItemRef>(p, "POST", `/api/v1/uploads/${uploadId}/complete`, { expectedSha256Hex: null, idempotencyKey: null });
}

export async function uploadToChannel(
  p: Person,
  teamSlug: string,
  channelName: string,
  name: string,
  content: string | Buffer = `content of ${name}`,
  opts: { folderId?: string | null; contentType?: string } = {},
): Promise<ItemRef> {
  const bytes = typeof content === "string" ? Buffer.from(content) : content;
  const start = await call<{ uploadId: string; partSizeBytes: number }>(p, "POST", "/api/v1/uploads/", {
    teamSlug,
    channelName,
    fileName: name,
    declaredSizeBytes: bytes.length,
    contentType: opts.contentType ?? "text/plain",
    folderId: opts.folderId ?? null,
  });
  return sendParts(p, start.uploadId, start.partSizeBytes, bytes);
}

export async function uploadPersonal(
  p: Person,
  name: string,
  content: string | Buffer = `personal ${name}`,
  opts: { folderId?: string | null; contentType?: string } = {},
): Promise<ItemRef> {
  const bytes = typeof content === "string" ? Buffer.from(content) : content;
  const start = await call<{ uploadId: string; partSizeBytes: number }>(p, "POST", "/api/v1/me/files/uploads", {
    fileName: name,
    declaredSizeBytes: bytes.length,
    contentType: opts.contentType ?? "text/plain",
    folderId: opts.folderId ?? null,
  });
  return sendParts(p, start.uploadId, start.partSizeBytes, bytes);
}

/** Uploads `count` small files concurrently (bounded) and returns them in upload order. */
export async function uploadMany(
  p: Person,
  teamSlug: string,
  channelName: string,
  count: number,
  name: (i: number) => string,
  opts: { folderId?: string | null; concurrency?: number } = {},
): Promise<ItemRef[]> {
  const results: ItemRef[] = new Array(count);
  let next = 0;
  const worker = async () => {
    while (next < count) {
      const i = next++;
      results[i] = await uploadToChannel(p, teamSlug, channelName, name(i), `file ${i}`, { folderId: opts.folderId });
    }
  };
  await Promise.all(Array.from({ length: Math.min(opts.concurrency ?? 6, count) }, worker));
  return results;
}

export async function trashItem(p: Person, teamSlug: string, channel: string, id: string) {
  await call(p, "DELETE", `/api/v1/teams/${teamSlug}/channels/${channel}/items/${id}`);
}

export async function restoreItem(p: Person, teamSlug: string, channel: string, id: string) {
  await call(p, "POST", `/api/v1/teams/${teamSlug}/channels/${channel}/items/${id}/restore`);
}

/** 1×1 PNG; the API re-encodes avatars, so any valid image works. */
export const TINY_PNG = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==",
  "base64",
);

export async function uploadAvatar(p: Person, png: Buffer = TINY_PNG) {
  await ok(
    await p.api.put("/api/v1/account/avatar", {
      headers: headers(p, { "Content-Type": "image/png" }),
      data: png,
    }),
    "avatar upload",
  );
}

export async function postMessage(
  p: Person,
  teamSlug: string,
  channel: string,
  body: string,
  opts: { replyToMessageId?: string; mentionedUserIds?: string[]; fileIds?: string[] } = {},
): Promise<{ id: string }> {
  return call(p, "POST", `/api/v1/teams/${teamSlug}/channels/${channel}/messages`, {
    body,
    replyToMessageId: opts.replyToMessageId ?? null,
    mentionedUserIds: opts.mentionedUserIds ?? [],
    fileIds: opts.fileIds ?? [],
  });
}

/** Waits until the app has rendered, "Loading…" placeholders are gone, and web fonts are ready. */
export async function settle(page: Page) {
  await page.waitForLoadState("domcontentloaded");
  await expect(page.locator("#root > *").first()).toBeVisible();
  await expect(page.getByText(/^Loading…$/)).toHaveCount(0, { timeout: 15_000 });
  await page.evaluate(() => document.fonts.ready.then(() => undefined));
}

/** axe critical/serious violations must be empty; the failure message names rule, help and targets. */
export async function expectAccessible(page: Page, label: string, opts: { exclude?: string[] } = {}) {
  let builder = new AxeBuilder({ page });
  for (const selector of opts.exclude ?? []) {
    builder = builder.exclude(selector);
  }
  const results = await builder.analyze();
  const serious = results.violations
    .filter((v) => v.impact === "critical" || v.impact === "serious")
    .map((v) => ({
      rule: v.id,
      help: v.help,
      nodes: v.nodes.slice(0, 5).map((n) => ({ target: n.target.join(" "), summary: n.failureSummary })),
    }));
  expect(serious, `${label}: ${JSON.stringify(serious, null, 2)}`).toEqual([]);
}

type People = {
  signUp: (label?: string, displayName?: string) => Promise<Person>;
  admin: () => Promise<Person>;
};

type TestFixtures = {
  /** Opens a fresh browser context signed in as `person` (optional theme), closed after the test. */
  openAs: (person: Person | null, opts?: { theme?: Theme }) => Promise<Page>;
};

export const test = base.extend<TestFixtures, { people: People }>({
  people: [
    async ({}, use) => {
      const created: Person[] = [];
      await use({
        signUp: async (label, displayName) => {
          const person = await signUp(label, displayName);
          created.push(person);
          return person;
        },
        admin: async () => {
          const person = await signInAdmin();
          created.push(person);
          return person;
        },
      });
      for (const person of created) {
        await person.api.dispose();
      }
    },
    { scope: "worker" },
  ],
  openAs: async ({ browser }, use) => {
    const contexts: BrowserContext[] = [];
    await use(async (person, opts) => {
      const context = await browser.newContext({
        baseURL: BASE_URL,
        reducedMotion: "reduce",
        storageState: person ? await person.api.storageState() : undefined,
      });
      const theme = opts?.theme ?? "light";
      await context.addInitScript((t) => localStorage.setItem("channeled-theme", t), theme);
      contexts.push(context);
      return context.newPage();
    });
    for (const context of contexts) {
      await context.close();
    }
  },
});
