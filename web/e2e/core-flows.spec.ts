import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import {
  E2E_ADMIN_EMAIL,
  E2E_ADMIN_PASSWORD,
  apiLogin,
  ensureFreshTeam,
  openRowActions,
  uiLogin,
  waitForUploadDone,
  waitForUploadedRow,
} from "./helpers";

test.describe.configure({ mode: "serial" });

test("sign in, upload, rename, move, trash, restore, join link, role, restricted, copy, audit + axe", async ({
  page,
  request,
}) => {
  // --- Sign in ---
  await uiLogin(page, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  const teamSlug = await ensureFreshTeam(page, "E2E Team");

  // --- Upload ---
  const fileName = `e2e-${Date.now()}.txt`;
  await page.getByTestId("file-input").setInputFiles({
    name: fileName,
    mimeType: "text/plain",
    buffer: Buffer.from("hello-e2e-upload"),
  });
  await waitForUploadedRow(page, `row-${fileName}`);

  // --- Rename ---
  const renamed = `renamed-${fileName}`;
  page.once("dialog", (d) => d.accept(renamed));
  await openRowActions(page, fileName);
  await page.getByRole("menuitem", { name: "Rename" }).click();
  await expect(page.getByTestId(`row-${renamed}`)).toBeVisible();

  // --- Move via picker into new folder ---
  await page.getByTestId("new-folder").click();
  await page.getByTestId("create-folder-name").fill("Docs");
  await page.getByTestId("create-folder-submit").click();
  await expect(page.getByTestId("create-folder-dialog")).toBeHidden({ timeout: 15_000 });
  await expect(page.getByTestId("row-Docs")).toBeVisible();

  await openRowActions(page, renamed);
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
  await openRowActions(page, renamed);
  await page.getByTestId(`trash-${renamed}`).click();
  await expect(page.getByTestId(`row-${renamed}`)).toHaveCount(0);

  // --- Restore ---
  const csrf = await apiLogin(request, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  await page.goto(`/t/${teamSlug}/c/general/trash`);
  await expect(page.getByTestId(`row-${renamed}`)).toBeVisible();
  await openRowActions(page, renamed);
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
  await page.getByTestId(`role-${inviteEmail}`).click();
  await page.getByTestId(`role-${inviteEmail}-option-Admin`).click();
  await expect(page.getByTestId(`role-${inviteEmail}`)).toHaveAttribute("data-value", "2");
  // Admins see every channel, so go back to Member before testing Restricted access.
  await page.getByTestId(`role-${inviteEmail}`).click();
  await page.getByTestId(`role-${inviteEmail}-option-Member`).click();
  await expect(page.getByTestId(`role-${inviteEmail}`)).toHaveAttribute("data-value", "1");

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
  await page.getByTestId("grant-manage-secret").click();
  await page.getByTestId(`grant-pick-secret-${inviteEmail}`).check();
  await page.getByTestId("grant-add-submit-secret").click();
  await expect(page.getByTestId(`grant-row-secret-${inviteEmail}`)).toBeVisible();
  await uiLogin(page, inviteEmail, "E2eMemberPassword1!");
  await page.goto(`/t/${teamSlug}/c/secret`);
  // Empty restricted channel shows empty-state (file-table only appears once items exist).
  await expect(page.getByTestId("empty-state")).toBeVisible();
  await expect(page.getByRole("heading", { name: "#secret" })).toBeVisible();

  // --- Personal copy to channel ---
  await page.goto("/me/files");
  const personalName = `personal-${Date.now()}.txt`;
  await page.getByTestId("personal-file-input").setInputFiles({
    name: personalName,
    mimeType: "text/plain",
    buffer: Buffer.from("personal-bytes"),
  });
  await waitForUploadedRow(page, `personal-row-${personalName}`);
  await openRowActions(page, personalName);
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
