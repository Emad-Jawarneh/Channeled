import {
  addMember,
  createFolder,
  createTeam,
  expect,
  expectAccessible,
  test,
  uploadMany,
  uploadToChannel,
  type Person,
  type TeamRef,
} from "./fixtures";

const pad = (i: number) => String(i).padStart(3, "0");
const TOTAL = 130;

let owner: Person;
let member: Person;
let team: TeamRef;

test.describe("cursor paging with Load more", () => {
  test.beforeAll(async ({ people }) => {
    test.setTimeout(240_000);
    owner = await people.signUp("Pager");
    member = await people.signUp("PagerMember");
    team = await createTeam(owner);
    await addMember(owner, team.slug, member);
    await createFolder(owner, team.slug, "general", "zz-folder");
    await createFolder(owner, team.slug, "general", "aa-folder");
    await uploadMany(owner, team.slug, "general", TOTAL, (i) => `file-${pad(i)}.txt`, { concurrency: 8 });
  });

  test("loads 50 at a time, keeps folders first, and announces new rows", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general`);
    const rows = page.locator("[data-testid^='row-']");
    await expect(rows).toHaveCount(50);
    await expect(rows.nth(0)).toHaveAttribute("data-testid", "row-aa-folder");
    await expect(rows.nth(1)).toHaveAttribute("data-testid", "row-zz-folder");
    await expect(rows.nth(2)).toHaveAttribute("data-testid", "row-file-000.txt");
    await expectAccessible(page, "paged list with Load more");

    await page.getByTestId("load-more").click();
    await expect(rows).toHaveCount(100);
    await expect(page.getByTestId("live-announcer")).toContainText("Loaded 50 more items");
    // Keyboard works too.
    await page.getByTestId("load-more").focus();
    await page.keyboard.press("Enter");
    await expect(rows).toHaveCount(TOTAL + 2);
    await expect(page.getByTestId("load-more")).toHaveCount(0);
    const names = await rows.evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")));
    expect(new Set(names).size).toBe(names.length);
    expect(names.at(-1)).toBe(`row-file-${pad(TOTAL - 1)}.txt`);
  });

  test("changing the sort resets to the first page in the new order", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general`);
    const rows = page.locator("[data-testid^='row-']");
    await page.getByTestId("load-more").click();
    await expect(rows).toHaveCount(100);
    await page.getByTestId("sort-col-name").click(); // name desc
    await expect(rows).toHaveCount(50);
    await expect(rows.nth(0)).toHaveAttribute("data-testid", "row-zz-folder");
    await expect(rows.nth(2)).toHaveAttribute("data-testid", `row-file-${pad(TOTAL - 1)}.txt`);
  });

  test("select all covers loaded rows only and says so", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general`);
    await page.getByTestId("select-all").check();
    await expect(page.getByTestId("bulk-count")).toHaveText("50 selected · loaded items only");
    await page.getByTestId("load-more").click();
    await expect(page.locator("[data-testid^='row-']")).toHaveCount(100);
    await expect(page.getByTestId("bulk-count")).toHaveText("50 selected · loaded items only");
    await expect(page.getByTestId(`check-file-060.txt`)).not.toBeChecked();
    // Changing the sort clears the selection instead of acting on rows that are no longer shown.
    await page.getByTestId("sort-col-name").click();
    await expect(page.getByTestId("bulk-bar")).toHaveCount(0);
  });

  test("a teammate's upload refreshes the loaded pages in place", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general`);
    const rows = page.locator("[data-testid^='row-']");
    await page.getByTestId("load-more").click();
    await expect(rows).toHaveCount(100);
    const lastLoaded = await rows.nth(99).getAttribute("data-testid");
    await uploadToChannel(member, team.slug, "general", "file-000a-from-member.txt");
    // Realtime refetches the same two pages: the new row appears in order and the old last row moves to page 3.
    await expect(page.getByTestId("row-file-000a-from-member.txt")).toBeVisible();
    await expect(rows).toHaveCount(100);
    await expect(page.getByTestId(lastLoaded!)).toHaveCount(0);
    await page.getByTestId("load-more").click();
    await expect(page.getByTestId(lastLoaded!)).toHaveCount(1);
  });
});
