import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD, apiLogin, uiLogin } from "./helpers";

test.describe.configure({ mode: "serial" });

test("sign in, upload, rename, move, trash, restore, invite, role, private, copy, audit + axe", async ({
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

  // --- Move (into new folder) ---
  await page.getByTestId("new-folder").click();
  await page.getByTestId("create-folder-name").fill("Docs");
  await page.getByTestId("create-folder-submit").click();
  await expect(page.getByTestId("row-Docs")).toBeVisible();

  // Get folder id from prompt path: click Move and accept folder id from prompt text is hard;
  // instead move to root after opening folder then back — use API for move target simplicity:
  const csrf = await apiLogin(request, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  const items = await request.get(`/api/v1/teams/${teamSlug}/channels/general/items`);
  const list = (await items.json()) as { items: { id: string; name: string; kind: number }[] };
  const folder = list.items.find((i) => i.name === "Docs" && i.kind === 1);
  const file = list.items.find((i) => i.name === renamed && i.kind === 0);
  expect(folder && file).toBeTruthy();
  const moved = await request.post(
    `/api/v1/teams/${teamSlug}/channels/general/items/${file!.id}/move`,
    {
      headers: { "X-CSRF-TOKEN": csrf, "Content-Type": "application/json" },
      data: { targetFolderId: folder!.id },
    },
  );
  expect(moved.ok()).toBeTruthy();
  await page.reload();
  await page.getByTestId("open-folder-Docs").click();
  await expect(page.getByTestId(`row-${renamed}`)).toBeVisible();

  // --- Delete (trash) ---
  await page.getByTestId(`actions-${renamed}`).click();
  await page.getByTestId(`trash-${renamed}`).click();
  await expect(page.getByTestId(`row-${renamed}`)).toHaveCount(0);

  // --- Restore ---
  await page.goto(`/t/${teamSlug}/c/general/trash`);
  await expect(page.getByTestId(`row-${renamed}`)).toBeVisible();
  await page.getByTestId(`actions-${renamed}`).click();
  await page.getByTestId(`restore-${renamed}`).click();
  await expect(page.getByTestId(`row-${renamed}`)).toHaveCount(0);
  await page.goto(`/t/${teamSlug}/c/general`);
  await page.getByTestId("open-folder-Docs").click();
  await expect(page.getByTestId(`row-${renamed}`)).toBeVisible({ timeout: 30_000 });

  // --- Invite + accept (platform invite for new user, then team invite) ---
  const inviteEmail = `e2e-member-${Date.now()}@example.com`;
  const inviteResp = await request.post("/api/v1/auth/invitations", {
    headers: { "X-CSRF-TOKEN": csrf, "Content-Type": "application/json" },
    data: { email: inviteEmail },
  });
  expect(inviteResp.ok()).toBeTruthy();
  const platformInvite = (await inviteResp.json()) as { rawToken: string };

  await page.goto(`/invite/${platformInvite.rawToken}`);
  await page.locator("select").selectOption("platform");
  await page.getByLabel("Display name").fill("E2E Member");
  await page.getByLabel("Password").fill("E2eMemberPassword1!");
  await page.getByRole("button", { name: "Accept" }).click();
  await expect(page.getByTestId("current-user")).toBeVisible();

  // Admin invites member to team
  await uiLogin(page, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  await page.goto(`/t/${teamSlug}/settings/members`);
  await page.getByTestId("invite-email").fill(inviteEmail);
  await page.getByTestId("invite-submit").click();
  await expect(page.getByTestId("invite-token")).toBeVisible();
  const tokenText = await page.getByTestId("invite-token").innerText();
  const teamToken = tokenText.match(/Invite token:\s*(\S+)/)?.[1];
  expect(teamToken).toBeTruthy();

  await uiLogin(page, inviteEmail, "E2eMemberPassword1!");
  await page.goto(`/invite/${teamToken}`);
  await page.locator("select").selectOption("team");
  await page.getByRole("button", { name: "Accept" }).click();
  await expect(page.getByTestId("current-user")).toBeVisible();

  // --- Role change ---
  await uiLogin(page, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  await page.goto(`/t/${teamSlug}/settings/members`);
  await page.getByTestId(`role-${inviteEmail}`).selectOption({ label: "Admin" });
  await expect(page.getByTestId(`role-${inviteEmail}`)).toHaveValue("2");

  // --- Private channel access ---
  await page.goto(`/t/${teamSlug}/settings/channels`);
  await page.getByTestId("channel-name").fill("secret");
  await page.getByTestId("channel-visibility").selectOption({ label: "Private" });
  await page.getByTestId("create-channel").click();
  await expect(page.getByTestId("settings-channel-secret")).toBeVisible();

  // Member without grant: channel list should hide private (or items 404)
  await uiLogin(page, inviteEmail, "E2eMemberPassword1!");
  await page.goto(`/t/${teamSlug}/c/general`);
  await expect(page.getByTestId("channel-secret")).toHaveCount(0);

  // Grant access then visible
  await uiLogin(page, E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD);
  await page.goto(`/t/${teamSlug}/settings/channels`);
  await page.getByTestId("grant-secret").selectOption({ label: inviteEmail });
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
