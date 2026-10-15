import { createTeam, expect, expectAccessible, test, uploadToChannel, type Person, type TeamRef } from "./fixtures";

let owner: Person;
let team: TeamRef;

test.describe("clipboard", () => {
  test.beforeAll(async ({ people }) => {
    owner = await people.signUp("Clipboard");
    team = await createTeam(owner);
    await uploadToChannel(owner, team.slug, "general", "cut-me.txt");
  });

  test("first cut shows clipboard tutorial, then chip; clear removes chip", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general`);
    await page.evaluate(() => localStorage.removeItem("channeled.clipboardTutorialSeen"));
    const row = page.getByTestId("row-cut-me.txt");
    await expect(row).toBeVisible();

    await row.click({ button: "right" });
    await page.getByRole("menuitem", { name: "Cut" }).click();
    await expect(page.getByTestId("clipboard-shelf")).toBeVisible();
    await expect(page.getByTestId("clipboard-tutorial")).toBeVisible();
    await expectAccessible(page, "clipboard tutorial visible");
    await page.getByTestId("clipboard-tutorial-dismiss").click();
    await expect(page.getByTestId("clipboard-tutorial")).toHaveCount(0);

    await expect(page.getByTestId("clipboard-chip")).toBeVisible();
    await expect(row).toHaveAttribute("data-cut", "true");

    await page.getByTestId("clipboard-clear").click();
    await expect(page.getByTestId("clipboard-chip")).toHaveCount(0);
    await expect(row).not.toHaveAttribute("data-cut", "true");
  });
});
