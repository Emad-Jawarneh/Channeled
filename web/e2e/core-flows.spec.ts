import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD, apiLogin, uiLogin } from "./helpers";

test.describe.configure({ mode: "serial" });

test("sign in, upload, rename, move, trash, restore, join link, role, restricted, copy, audit + axe", async ({
  page,
  request,
}) => {
  // --- Sign in ---
  await uiLogin(page, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);

  // Create team + general if landing on welcome
  if (await page.getByTestId("create-team").isVisible().catch(() => false)) {
    await page.getByTestId("create-team-name").fill(`E2E Team ${Date.now()}`);
    await page.getByTestId("create-team").click();
  }
  await expect(page).toHaveURL(/\/t\/.+\/c\/general/);

  const teamSlug = page.url().match(/\/t\/([^/]+)\//)?.[1];
  expect(teamSlug).toBeTruthy();

  // --- Upload ---
  const fileName = `e2e-${Date.now()}.txt`;
  await page.getByTestId("file-input").setInputFiles({
    name: fileName,
    mimeType: "text/plain",
    buffer: Buffer.from("hello-e2e-upload"),
  });
  await expect(page.getByText("done · 100%")).toBeVisible({ timeout: 60_000 });
  await expect(page.getByTestId(`row-${fileName}`)).toBeVisible({ timeout: 30_000 });

  // --- Rename ---
  const renamed = `renamed-${fileName}`;
  page.once("dialog", (d) => d.accept(renamed));
  await page.getByTestId(`actions-${fileName}`).click();
  await page.getByRole("menuitem", { name: "Rename" }).click();
  await expect(page.getByTestId(`row-${renamed}`)).toBeVisible();

  // --- Move via picker into new folder ---
  await page.getByTestId("new-folder").click();
  await page.getByTestId("create-folder-name").fill("Docs");
  await page.getByTestId("create-folder-submit").click();
  await expect(page.getByTestId("row-Docs")).toBeVisible();

  await page.getByTestId(`actions-${renamed}`).click();
  await page.getByTestId(`move-${renamed}`).click();
  await page.getByTestId("move-to-dialog-select").selectOption({ label: "Docs" });
  await page.getByTestId("move-to-dialog-submit").click();
  await expect(page.getByTestId(`row-${renamed}`)).toHaveCount(0);

  await page.getByTestId("open-folder-Docs").click();
  await expect(page).toHaveURL(/folder=/);
  await expect(page.getByTestId("folder-breadcrumbs")).toBeVisible();
  await expect(page.getByTestId("crumb-root")).toBeVisible();
  await expect(page.getByTestId(`row-${renamed}`)).toBeVisible();

  // Back returns to parent (root)
  await page.goBack();
  await expect(page).not.toHaveURL(/folder=/);
  await expect(page.getByTestId("row-Docs")).toBeVisible();

  // Re-enter for trash/restore
  await page.getByTestId("open-folder-Docs").click();
  await expect(page.getByTestId(`row-${renamed}`)).toBeVisible();

  // --- Delete (trash) ---
  await page.getByTestId(`actions-${renamed}`).click();
  await page.getByTestId(`trash-${renamed}`).click();
  await expect(page.getByTestId(`row-${renamed}`)).toHaveCount(0);

  // --- Restore ---
  const csrf = await apiLogin(request, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  await page.goto(`/t/${teamSlug}/c/general/trash`);
  await expect(page.getByTestId(`row-${renamed}`)).toBeVisible();
  await page.getByTestId(`actions-${renamed}`).click();
  await page.getByTestId(`restore-${renamed}`).click();
  await expect(page.getByTestId(`row-${renamed}`)).toHaveCount(0);
  await page.goto(`/t/${teamSlug}/c/general`);
  await page.getByTestId("open-folder-Docs").click();
  await expect(page.getByTestId(`row-${renamed}`)).toBeVisible({ timeout: 30_000 });

  // --- Platform invite for a new user, then team join link ---
  const inviteEmail = `e2e-member-${Date.now()}@example.com`;
  const inviteResp = await request.post("/api/v1/auth/invitations", {
    headers: { "X-CSRF-TOKEN": csrf, "Content-Type": "application/json" },
    data: { email: inviteEmail },
  });
  expect(inviteResp.ok()).toBeTruthy();
  const platformInvite = (await inviteResp.json()) as { rawToken: string };

  await page.goto(`/invite/${platformInvite.rawToken}`);
  await page.getByLabel("Display name").fill("E2E Member");
  await page.getByLabel("Password").fill("E2eMemberPassword1!");
  await page.getByRole("button", { name: "Accept" }).click();
  await expect(page.getByTestId("current-user")).toBeVisible();

  // Owner switches the team to Open join (default role Member) and fetches the team join link
  const settingsResp = await request.put(`/api/v1/teams/${teamSlug}/settings`, {
    headers: { "X-CSRF-TOKEN": csrf, "Content-Type": "application/json" },
    data: { joinMode: 0, defaultJoinRole: 1, confirmOpenJoinApprovePending: true },
  });
  expect(settingsResp.ok()).toBeTruthy();
  const linkResp = await request.post(`/api/v1/teams/${teamSlug}/join-link`, {
    headers: { "X-CSRF-TOKEN": csrf },
  });
  expect(linkResp.ok()).toBeTruthy();
  const joinLink = (await linkResp.json()) as { joinPath: string };

  // New user joins through the link
  await uiLogin(page, inviteEmail, "E2eMemberPassword1!");
  await page.goto(joinLink.joinPath);
  await page.getByTestId("join-team").click();
  await expect(page).toHaveURL(/\/t\/.+\/c\/general/);

  // --- Role change (Owner only) ---
  await uiLogin(page, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  await page.goto(`/t/${teamSlug}/settings/members`);
  await page.getByTestId(`role-${inviteEmail}`).selectOption({ label: "Admin" });
  await expect(page.getByTestId(`role-${inviteEmail}`)).toHaveValue("2");
  // Admins see every channel, so go back to Member before testing Restricted access.
  await page.getByTestId(`role-${inviteEmail}`).selectOption({ label: "Member" });
  await expect(page.getByTestId(`role-${inviteEmail}`)).toHaveValue("1");

  // --- Restricted channel access ---
  await page.goto(`/t/${teamSlug}/settings/channels`);
  await page.getByTestId("channel-name").fill("secret");
  await page.getByTestId("channel-visibility").selectOption({ label: "Restricted" });
  await page.getByTestId("create-channel").click();
  await expect(page.getByTestId("settings-channel-secret")).toBeVisible();

  // Member without grant: channel list should hide the restricted channel
  await uiLogin(page, inviteEmail, "E2eMemberPassword1!");
  await page.goto(`/t/${teamSlug}/c/general`);
  await expect(page.getByTestId("channel-secret")).toHaveCount(0);

  // Grant access then visible
  await uiLogin(page, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  await page.goto(`/t/${teamSlug}/settings/channels`);
  await page.getByTestId(`grant-pick-secret-${inviteEmail}`).check();
  await page.getByTestId("grant-add-submit-secret").click();
  await expect(page.getByTestId(`grant-row-secret-${inviteEmail}`)).toBeVisible();
  await uiLogin(page, inviteEmail, "E2eMemberPassword1!");
  await page.goto(`/t/${teamSlug}/c/secret`);
  await expect(page.getByTestId("file-table")).toBeVisible();

  // --- Personal copy to channel ---
  await page.goto("/me/files");
  const personalName = `personal-${Date.now()}.txt`;
  await page.getByTestId("personal-file-input").setInputFiles({
    name: personalName,
    mimeType: "text/plain",
    buffer: Buffer.from("personal-bytes"),
  });
  await expect(page.getByText("done · 100%").first()).toBeVisible({ timeout: 60_000 });
  await expect(page.getByTestId(`personal-row-${personalName}`)).toBeVisible({ timeout: 30_000 });
  await page.getByTestId(`actions-${personalName}`).click();
  await page.getByTestId(`copy-${personalName}`).click();
  await expect(page.getByTestId("copy-success")).toBeVisible();

  // --- Admin audit ---
  await uiLogin(page, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  await page.goto("/admin/audit");
  await expect(page.getByTestId("admin-audit")).toBeVisible();
  await expect(page.locator("[data-testid^='audit-']").first()).toBeVisible();

  // --- Accessibility ---
  await page.goto(`/t/${teamSlug}/c/general`);
  const results = await new AxeBuilder({ page }).analyze();
  const serious = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
  expect(serious, JSON.stringify(serious, null, 2)).toEqual([]);
});
