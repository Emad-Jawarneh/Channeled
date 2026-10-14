import { expect, test } from "@playwright/test";

/**
 * Clipboard cut/copy/paste smoke. Animations are disabled via playwright.config reducedMotion.
 * Relies on e2e harness from scripts/e2e.sh (API + preview already up when run via that script).
 */
test.describe("clipboard", () => {
  test("cut shows chip and dimmed row; clear removes chip", async ({ page }) => {
    await page.goto("/login");
    const email = process.env.E2E_ADMIN_EMAIL ?? "e2e-admin@example.com";
    const password = process.env.E2E_ADMIN_PASSWORD ?? "E2eAdminPassword1!";
    await page.getByLabel(/email/i).fill(email);
    await page.getByLabel(/password/i).fill(password);
    await page.getByRole("button", { name: /sign in/i }).click();
    await expect(page).not.toHaveURL(/\/login/);

    // Prefer an existing team file channel from core flows if present.
    const filesLink = page.getByRole("link", { name: /files|general/i }).first();
    if (await filesLink.count()) {
      await filesLink.click();
    }

    const row = page.locator("[data-testid^='row-']").first();
    if ((await row.count()) === 0) {
      test.skip(true, "No file rows available in this environment");
      return;
    }

    await row.click({ button: "right" });
    const cut = page.getByRole("menuitem", { name: "Cut" });
    await expect(cut).toBeVisible();
    await cut.click();
    await expect(page.getByTestId("clipboard-chip")).toBeVisible();
    await expect(row).toHaveAttribute("data-cut", "true");
    await page.getByTestId("clipboard-clear").click();
    await expect(page.getByTestId("clipboard-chip")).toHaveCount(0);
  });
});
