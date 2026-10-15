import type { Page } from "@playwright/test";
import {
  Kind,
  addMember,
  createChannel,
  createTeam,
  expect,
  expectAccessible,
  postMessage,
  test,
  uploadAvatar,
  type Person,
  type TeamRef,
} from "./fixtures";

let owner: Person;
let member: Person;
let team: TeamRef;

/** Shift+Tab from the composer until a message (roving tab stop) holds focus. */
async function tabBackToMessage(page: Page) {
  for (let i = 0; i < 12; i++) {
    await page.keyboard.press("Shift+Tab");
    const isMessage = await page.evaluate(() => Boolean((document.activeElement as HTMLElement | null)?.dataset.messageId));
    if (isMessage) return;
  }
  throw new Error("Shift+Tab never reached a message");
}

async function focusedMessageId(page: Page): Promise<string | undefined> {
  return page.evaluate(() => (document.activeElement as HTMLElement | null)?.dataset.messageId);
}

test.describe("chat polish and keyboard access", () => {
  test.beforeAll(async ({ people }) => {
    owner = await people.signUp("ChatOwner", "Chat Owner");
    member = await people.signUp("ChatMember", "Chat Member");
    team = await createTeam(owner);
    await addMember(owner, team.slug, member);
    await uploadAvatar(member);
  });

  test("groups consecutive messages and shows avatars from the profile", async ({ openAs }) => {
    await createChannel(owner, team.slug, "grouping", { kind: Kind.Text });
    await postMessage(member, team.slug, "grouping", "first from member");
    await postMessage(member, team.slug, "grouping", "second from member");
    await postMessage(owner, team.slug, "grouping", "owner replies");

    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/grouping`);
    const rows = page.getByTestId("chat-message");
    await expect(rows).toHaveCount(3);
    await expect(rows.nth(0)).toHaveAttribute("data-grouped", "false");
    await expect(rows.nth(1)).toHaveAttribute("data-grouped", "true");
    await expect(rows.nth(2)).toHaveAttribute("data-grouped", "false");
    // The member uploaded an avatar; the owner did not.
    await expect(rows.nth(0).getByTestId("chat-avatar")).toHaveAttribute("data-avatar", "image");
    await expect(rows.nth(1).getByTestId("chat-avatar")).toHaveCount(0);
    await expect(rows.nth(2).getByTestId("chat-avatar")).toHaveAttribute("data-avatar", "initials");
    await expectAccessible(page, "grouped chat");
  });

  test("keyboard only: move between messages, reply with R, delete with confirmation", async ({ openAs }) => {
    await createChannel(owner, team.slug, "keys", { kind: Kind.Text });
    const older = await postMessage(member, team.slug, "keys", "older message");
    await postMessage(owner, team.slug, "keys", "newest message");

    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/keys`);
    await expect(page.getByTestId("chat-message")).toHaveCount(2);
    // Writers land in the composer.
    await expect(page.getByTestId("chat-input")).toBeFocused();

    await tabBackToMessage(page);
    await page.keyboard.press("ArrowUp");
    expect(await focusedMessageId(page)).toBe(older.id);

    await page.keyboard.press("r");
    await expect(page.getByTestId("chat-reply-chip")).toContainText("Chat Member");
    await expect(page.getByTestId("chat-input")).toBeFocused();
    await page.keyboard.type("keyboard reply");
    await page.keyboard.press("Enter");
    const rows = page.getByTestId("chat-message");
    await expect(rows).toHaveCount(3);
    await expect(rows.nth(2).getByTestId("chat-reply-quote")).toContainText("older message");

    // Delete the reply with the keyboard: Delete opens the dialog (Cancel focused), Tab + Enter confirms.
    await tabBackToMessage(page);
    await page.keyboard.press("End");
    await page.keyboard.press("Delete");
    const dialog = page.getByTestId("chat-delete-dialog");
    await expect(dialog).toBeVisible();
    await expectAccessible(page, "delete message dialog");
    await page.keyboard.press("Escape");
    await expect(dialog).toBeHidden();
    await expect(rows).toHaveCount(3);

    await page.keyboard.press("Delete");
    await expect(dialog).toBeVisible();
    await expect(page.getByTestId("chat-delete-dialog-cancel")).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(page.getByTestId("chat-delete-dialog-submit")).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(rows).toHaveCount(2);
    await expect(page.getByText("keyboard reply")).toHaveCount(0);
  });

  test("Shift+click delete skips the confirmation", async ({ openAs }) => {
    await createChannel(owner, team.slug, "shift", { kind: Kind.Text });
    await postMessage(owner, team.slug, "shift", "delete me quickly");
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/shift`);
    const row = page.getByTestId("chat-message").first();
    await row.hover();
    await row.getByTestId("chat-delete").click({ modifiers: ["Shift"] });
    await expect(page.getByTestId("chat-delete-dialog")).toBeHidden();
    await expect(page.getByTestId("chat-message")).toHaveCount(0);
  });

  test("channel switches move focus and announce the channel", async ({ openAs }) => {
    await createChannel(owner, team.slug, "lobby", { kind: Kind.Text });
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/lobby`);
    await expect(page.getByTestId("chat-input")).toBeFocused();
    await expect(page.getByTestId("live-announcer")).toHaveText("#lobby, text channel");

    await page.getByTestId("channel-general").click();
    await expect(page.getByRole("heading", { level: 2, name: "general" })).toBeFocused();
    await expect(page.getByTestId("live-announcer")).toHaveText("#general, file channel");

    await page.getByTestId("channel-lobby").click();
    await expect(page.getByTestId("chat-input")).toBeFocused();
    await expect(page.getByTestId("live-announcer")).toHaveText("#lobby, text channel");
  });
});
