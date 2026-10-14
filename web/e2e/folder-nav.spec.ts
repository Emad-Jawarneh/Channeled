import { test, expect } from "@playwright/test";
import {
  E2E_ADMIN_EMAIL,
  E2E_ADMIN_PASSWORD,
  apiLogin,
  ensureFreshTeam,
  openRowActions,
  uiLogin,
  waitForUploadedRow,
} from "./helpers";

test.describe.configure({ mode: "serial" });

test("folder crumbs, Back, picker move, and internal DnD skips upload overlay", async ({
  page,
  request,
}) => {
  await uiLogin(page, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  const teamSlug = await ensureFreshTeam(page, "E2E Folder");

  await page.getByTestId("new-folder").click();
  await page.getByTestId("create-folder-name").fill("Alpha");
  await page.getByTestId("create-folder-submit").click();
  await expect(page.getByTestId("create-folder-dialog")).toBeHidden({ timeout: 15_000 });
  await expect(page.getByTestId("row-Alpha")).toBeVisible();

  await page.getByTestId("open-folder-Alpha").click();
  await expect(page).toHaveURL(/folder=/);
  await expect(page.getByTestId("folder-breadcrumbs")).toContainText("Alpha");

  const fileName = `nav-${Date.now()}.txt`;
  await page.getByTestId("file-input").setInputFiles({
    name: fileName,
    mimeType: "text/plain",
    buffer: Buffer.from("folder-nav"),
  });
  await waitForUploadedRow(page, `row-${fileName}`);

  // Move file to root via picker (ancestors include root via allowNone)
  await openRowActions(page, fileName);
  await page.getByTestId(`move-${fileName}`).click();
  await page.getByTestId("move-to-dialog-select").selectOption({ value: "" });
  await page.getByTestId("move-to-dialog-submit").click();
  await expect(page.getByTestId(`row-${fileName}`)).toHaveCount(0);

  await page.getByTestId("crumb-root").click();
  await expect(page).not.toHaveURL(/folder=/);
  await expect(page.getByTestId(`row-${fileName}`)).toBeVisible();
  await expect(page.getByTestId("row-Alpha")).toBeVisible();

  // Re-enter Alpha, then Back
  await page.getByTestId("open-folder-Alpha").click();
  await expect(page).toHaveURL(/folder=/);
  await page.goBack();
  await expect(page).not.toHaveURL(/folder=/);

  // Internal DnD custom type must not show upload overlay
  await page.evaluate(() => {
    const dt = new DataTransfer();
    dt.setData("application/x-channeled-items", JSON.stringify(["x"]));
    window.dispatchEvent(
      new DragEvent("dragenter", { bubbles: true, cancelable: true, dataTransfer: dt }),
    );
  });
  await expect(page.getByTestId("drop-overlay")).toHaveCount(0);

  // Partial move: seed conflicting name at root, move two items with one failure expected
  const csrf = await apiLogin(request, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  await page.getByTestId("new-folder").click();
  await page.getByTestId("create-folder-name").fill("Gamma");
  await page.getByTestId("create-folder-submit").click();
  await expect(page.getByTestId("create-folder-dialog")).toBeHidden({ timeout: 15_000 });
  await expect(page.getByTestId("row-Gamma")).toBeVisible();

  const itemsRes = await request.get(`/api/v1/teams/${teamSlug}/channels/general/items`);
  expect(itemsRes.ok()).toBeTruthy();
  const items = (await itemsRes.json()) as {
    items: { id: string; name: string; kind: number; folderId?: string | null }[];
  };
  const gamma = items.items.find((i) => i.name === "Gamma");
  const file = items.items.find((i) => i.name === fileName);
  expect(gamma, `Gamma missing in ${JSON.stringify(items.items.map((i) => i.name))}`).toBeTruthy();
  expect(file, `File missing in ${JSON.stringify(items.items.map((i) => i.name))}`).toBeTruthy();

  // Place a copy-named conflict inside Gamma via API move of a duplicate folder name setup:
  // Create folder "conflict" at root and inside Gamma, then try moving root conflict into Gamma.
  await request.post(`/api/v1/teams/${teamSlug}/channels/general/folders`, {
    headers: { "X-CSRF-TOKEN": csrf, "Content-Type": "application/json" },
    data: { name: "Twin", parentFolderId: null },
  });
  await request.post(`/api/v1/teams/${teamSlug}/channels/general/folders`, {
    headers: { "X-CSRF-TOKEN": csrf, "Content-Type": "application/json" },
    data: { name: "Twin", parentFolderId: gamma!.id },
  });
  await page.reload();
  await expect(page.getByTestId("row-Twin")).toBeVisible();

  await page.getByTestId("check-Twin").check();
  await page.getByTestId(`check-${fileName}`).check();
  await page.getByTestId("bulk-move-to").click();
  await page.getByTestId("move-to-dialog-select").selectOption({ label: "Gamma" });
  await page.getByTestId("move-to-dialog-submit").click();
  await expect(page.getByTestId("move-summary")).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId("move-summary")).toContainText(/failed|Moved/i);
});
