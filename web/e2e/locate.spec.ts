import {
  Kind,
  call,
  createChannel,
  createFolder,
  createPersonalFolder,
  createTeam,
  expect,
  expectAccessible,
  postMessage,
  test,
  trashItem,
  uploadMany,
  uploadPersonal,
  type ItemRef,
  type Person,
  type TeamRef,
} from "./fixtures";

const pad = (i: number) => String(i).padStart(3, "0");

let owner: Person;
let team: TeamRef;
let archive: ItemRef;
let deepFile: ItemRef;
let personalDeep: ItemRef;

test.describe("open an item on any page (locate)", () => {
  test.beforeAll(async ({ people }) => {
    test.setTimeout(240_000);
    owner = await people.signUp("Locator");
    team = await createTeam(owner);
    archive = await createFolder(owner, team.slug, "general", "Archive");
    const files = await uploadMany(owner, team.slug, "general", 130, (i) => `doc-${pad(i)}.txt`, {
      folderId: archive.id,
      concurrency: 8,
    });
    deepFile = files[120]!; // page 3 in name order
    await createChannel(owner, team.slug, "chat", { kind: Kind.Text });
    await postMessage(owner, team.slug, "chat", "See the old doc", { fileIds: [deepFile.id] });

    const box = await createPersonalFolder(owner, "Box");
    let target: ItemRef | null = null;
    for (let i = 0; i < 70; i++) {
      const f = await uploadPersonal(owner, `note-${pad(i)}.txt`, `n${i}`, { folderId: box.id });
      if (i === 65) target = f;
    }
    personalDeep = target!;
  });

  test("a chat file chip opens its folder and pins the row from a later page", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/chat`);
    // The chip becomes a link once the file reference resolves.
    const chip = page.getByRole("link", { name: deepFile.name });
    await expect(chip).toHaveAttribute("href", `/open/${deepFile.id}`);
    await chip.click();
    await expect(page).toHaveURL(new RegExp(`/t/${team.slug}/c/general\\?folder=${archive.id}`));
    const row = page.getByTestId(`row-${deepFile.name}`);
    await expect(row).toHaveAttribute("data-pinned", "true");
    await expect(row).toHaveAttribute("data-selected", "true");
    await expect(row.getByTestId("pinned-pill")).toHaveText("Found");
    await expect(row).toBeFocused();
    // Only the first page (plus the pinned row) is loaded.
    await expect(page.locator("[data-testid^='row-']")).toHaveCount(51);
    await expectAccessible(page, "pinned located row");

    // Loading pages until the real row arrives replaces the pin without duplicating it.
    await page.getByTestId("load-more").click();
    await page.getByTestId("load-more").click();
    await expect(page.getByTestId(`row-${deepFile.name}`)).toHaveCount(1);
    await expect(page.getByTestId(`row-${deepFile.name}`)).not.toHaveAttribute("data-pinned", "true");
  });

  test("a deep link to a personal file on a later page pins it", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/open/${personalDeep.id}`);
    await expect(page).toHaveURL(/\/me\/files\?folder=/);
    const row = page.getByTestId(`personal-row-${personalDeep.name}`);
    await expect(row).toHaveAttribute("data-pinned", "true");
    await expect(row).toHaveAttribute("data-selected", "true");
  });

  test("a moved file opens in its new folder", async ({ openAs }) => {
    const moved = (await uploadMany(owner, team.slug, "general", 1, () => "moving.txt"))[0]!;
    const target = await createFolder(owner, team.slug, "general", "Moved here");
    await call(owner, "POST", `/api/v1/teams/${team.slug}/channels/general/items/${moved.id}/move`, {
      targetFolderId: target.id,
      targetChannelName: null,
    });
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general?item=${moved.id}`);
    await expect(page).toHaveURL(new RegExp(`folder=${target.id}`));
    await expect(page.getByTestId("row-moving.txt")).toHaveAttribute("data-selected", "true");
  });

  test("a trashed file opens the trash view with its entry selected", async ({ openAs }) => {
    const doomed = (await uploadMany(owner, team.slug, "general", 1, () => "doomed.txt"))[0]!;
    await trashItem(owner, team.slug, "general", doomed.id);
    const page = await openAs(owner);
    await page.goto(`/open/${doomed.id}`);
    await expect(page).toHaveURL(new RegExp(`/t/${team.slug}/c/general/trash`));
    await expect(page.getByTestId("row-doomed.txt")).toHaveAttribute("data-selected", "true");
  });

  test("an unknown or inaccessible item shows a clear message", async ({ openAs, people }) => {
    const stranger = await people.signUp("Stranger");
    const page = await openAs(stranger);
    await page.goto(`/open/${deepFile.id}`);
    await expect(page.getByTestId("open-item-missing")).toContainText("Item unavailable");
    await expectAccessible(page, "open item missing");
  });
});
