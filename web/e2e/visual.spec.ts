import type { Page } from "@playwright/test";
import {
  Kind,
  addMember,
  createChannel,
  createFolder,
  createTeam,
  expect,
  postMessage,
  settle,
  test,
  uploadToChannel,
  type Person,
  type TeamRef,
  type Theme,
} from "./fixtures";

/**
 * Screenshot baselines for the main surfaces in both themes. Names, sizes and message bodies are fixed;
 * timestamps, day dividers, random-id avatar colours and join URLs are masked. Baselines are written on the
 * first run (updateSnapshots: "missing") and only replaced on purpose with --update-snapshots.
 */
let owner: Person;
let member: Person;
let team: TeamRef;

function volatile(page: Page) {
  return [
    page.locator("time"),
    page.getByTestId("modified-at"),
    page.getByTestId("chat-avatar"),
    page.locator("[role='separator']"),
    page.getByTestId("team-join-url"),
    page.getByTestId("account-username"),
  ];
}

test.describe("visual baselines", () => {
  test.beforeAll(async ({ people }) => {
    owner = await people.signUp("Visual", "Visual Owner");
    member = await people.signUp("Visual", "Visual Member");
    team = await createTeam(owner, "Visual Team");
    await addMember(owner, team.slug, member);
    await createFolder(owner, team.slug, "general", "Designs");
    await uploadToChannel(owner, team.slug, "general", "brief.txt", "x".repeat(2048));
    await uploadToChannel(owner, team.slug, "general", "roadmap.txt", "y".repeat(4096));
    await createChannel(owner, team.slug, "chat", { kind: Kind.Text });
    const hello = await postMessage(member, team.slug, "chat", "Morning! The brief is in #general.");
    await postMessage(member, team.slug, "chat", "Second message in the same group.");
    await postMessage(owner, team.slug, "chat", `Thanks @${member.displayName}, looking now.`, {
      replyToMessageId: hello.id,
      mentionedUserIds: [member.id],
    });
  });

  for (const theme of ["light", "dark"] as Theme[]) {
    test(`file list · ${theme}`, async ({ openAs }) => {
      const page = await openAs(owner, { theme });
      await page.goto(`/t/${team.slug}/c/general`);
      await expect(page.getByTestId("row-roadmap.txt")).toBeVisible();
      await settle(page);
      await expect(page).toHaveScreenshot(`file-list-${theme}.png`, { mask: volatile(page) });
    });

    test(`chat · ${theme}`, async ({ openAs }) => {
      const page = await openAs(owner, { theme });
      await page.goto(`/t/${team.slug}/c/chat`);
      await expect(page.getByTestId("chat-message")).toHaveCount(3);
      await settle(page);
      await expect(page).toHaveScreenshot(`chat-${theme}.png`, { mask: volatile(page) });
    });

    test(`team settings card · ${theme}`, async ({ openAs }) => {
      const page = await openAs(owner, { theme });
      await page.goto(`/t/${team.slug}/settings/general`);
      const card = page.getByTestId("team-profile-card");
      await expect(card).toBeVisible();
      await settle(page);
      await expect(card).toHaveScreenshot(`team-details-card-${theme}.png`, { mask: volatile(page) });
    });

    test(`account profile · ${theme}`, async ({ openAs }) => {
      const page = await openAs(owner, { theme });
      await page.goto("/account/profile");
      const card = page.getByTestId("account-profile-card");
      await expect(card).toBeVisible();
      await settle(page);
      await expect(card).toHaveScreenshot(`account-profile-card-${theme}.png`, { mask: volatile(page) });
    });
  }
});
