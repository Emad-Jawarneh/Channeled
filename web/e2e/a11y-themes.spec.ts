import {
  Kind,
  addMember,
  createChannel,
  createFolder,
  createPersonalFolder,
  createTeam,
  expect,
  expectAccessible,
  postMessage,
  settle,
  test,
  trashItem,
  uploadPersonal,
  uploadToChannel,
  type Person,
  type TeamRef,
  type Theme,
} from "./fixtures";
import type { Page } from "@playwright/test";

type Who = "owner" | "admin" | "loner" | "anon";
type Route = { label: string; who: Who; path: () => string; ready?: (page: Page) => Promise<void> };

let owner: Person;
let member: Person;
let admin: Person;
let loner: Person;
let team: TeamRef;
let folderId = "";
let joinToken = "";

const routes: Route[] = [
  { label: "landing", who: "anon", path: () => "/" },
  { label: "login", who: "anon", path: () => "/login" },
  { label: "signup", who: "anon", path: () => "/signup" },
  { label: "join (signed out)", who: "anon", path: () => `/join/${joinToken}` },
  { label: "share (unavailable)", who: "anon", path: () => "/s/not-a-real-token" },
  { label: "welcome (no team)", who: "loner", path: () => "/welcome" },
  {
    label: "file channel",
    who: "owner",
    path: () => `/t/${team.slug}/c/general`,
    ready: (p) => expect(p.getByTestId("row-a11y-report.txt")).toBeVisible(),
  },
  {
    label: "file channel folder",
    who: "owner",
    path: () => `/t/${team.slug}/c/general?folder=${folderId}`,
    ready: (p) => expect(p.getByTestId("folder-breadcrumbs")).toBeVisible(),
  },
  {
    label: "file channel trash",
    who: "owner",
    path: () => `/t/${team.slug}/c/general/trash`,
    ready: (p) => expect(p.getByTestId("row-a11y-trashed.txt")).toBeVisible(),
  },
  {
    label: "text channel",
    who: "owner",
    path: () => `/t/${team.slug}/c/chat`,
    ready: (p) => expect(p.getByTestId("chat-message").first()).toBeVisible(),
  },
  { label: "team settings general", who: "owner", path: () => `/t/${team.slug}/settings/general` },
  { label: "team settings members", who: "owner", path: () => `/t/${team.slug}/settings/members` },
  { label: "team settings channels", who: "owner", path: () => `/t/${team.slug}/settings/channels` },
  { label: "team settings storage", who: "owner", path: () => `/t/${team.slug}/settings/storage` },
  {
    label: "personal files",
    who: "owner",
    path: () => "/me/files",
    ready: (p) => expect(p.getByTestId("personal-row-a11y-personal.txt")).toBeVisible(),
  },
  { label: "personal recent", who: "owner", path: () => "/me/files/recent" },
  { label: "personal starred", who: "owner", path: () => "/me/files/starred" },
  { label: "personal trash", who: "owner", path: () => "/me/files/trash" },
  { label: "search", who: "owner", path: () => "/search" },
  { label: "account profile", who: "owner", path: () => "/account/profile" },
  { label: "account security", who: "owner", path: () => "/account/security" },
  { label: "account sessions", who: "owner", path: () => "/account/sessions" },
  { label: "admin dashboard", who: "admin", path: () => "/admin/dashboard" },
  { label: "admin users", who: "admin", path: () => "/admin/users" },
  { label: "admin teams", who: "admin", path: () => "/admin/teams" },
  { label: "admin audit", who: "admin", path: () => "/admin/audit" },
  { label: "admin health", who: "admin", path: () => "/admin/health" },
  { label: "admin storage", who: "admin", path: () => "/admin/storage" },
];

test.describe("accessibility in light and dark themes", () => {
  test.beforeAll(async ({ people }) => {
    owner = await people.signUp("Owner");
    member = await people.signUp("Member");
    admin = await people.admin();
    loner = await people.signUp("Loner");
    team = await createTeam(owner);
    await addMember(owner, team.slug, member);
    const folder = await createFolder(owner, team.slug, "general", "Reports");
    folderId = folder.id;
    await uploadToChannel(owner, team.slug, "general", "a11y-report.txt");
    await uploadToChannel(owner, team.slug, "general", "a11y-nested.txt", "nested", { folderId });
    const trashed = await uploadToChannel(owner, team.slug, "general", "a11y-trashed.txt");
    await trashItem(owner, team.slug, "general", trashed.id);
    await createChannel(owner, team.slug, "chat", { kind: Kind.Text });
    const first = await postMessage(member, team.slug, "chat", "Hello from the member");
    await postMessage(owner, team.slug, "chat", `Thanks @${member.displayName}`, {
      replyToMessageId: first.id,
      mentionedUserIds: [member.id],
    });
    await createPersonalFolder(owner, "Private");
    await uploadPersonal(owner, "a11y-personal.txt");
    const link = await owner.api.post(`/api/v1/teams/${team.slug}/join-link`, {
      headers: { "X-CSRF-TOKEN": owner.csrf },
    });
    joinToken = ((await link.json()) as { joinPath: string }).joinPath.split("/join/")[1]!;
  });

  for (const theme of ["light", "dark"] as Theme[]) {
    for (const route of routes) {
      test(`${route.label} · ${theme}`, async ({ openAs }) => {
        const person = { owner, admin, loner, anon: null }[route.who];
        const page = await openAs(person, { theme });
        await page.goto(route.path());
        await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
        if (route.ready) await route.ready(page);
        await settle(page);
        await expectAccessible(page, `${route.label} (${theme})`);
      });
    }

    test(`open overlays · ${theme}`, async ({ openAs }) => {
      const page = await openAs(owner, { theme });
      await page.goto(`/t/${team.slug}/c/general`);
      await settle(page);
      await page.getByTestId("notification-bell").click();
      await expect(page.getByTestId("notification-panel")).toBeVisible();
      await expectAccessible(page, `notifications panel (${theme})`);
      await page.getByTestId("notification-bell").click();
      await page.getByTestId("new-channel").click();
      await expectAccessible(page, `create channel dialog (${theme})`);
    });
  }
});
