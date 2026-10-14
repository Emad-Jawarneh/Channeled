import { expect, type APIRequestContext, type Page } from "@playwright/test";

export const E2E_ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? "e2e-admin@example.com";
export const E2E_ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? "E2eAdminPassword1!";

export async function apiLogin(request: APIRequestContext, email: string, password: string) {
  const login = await request.post("/api/v1/auth/login", {
    data: { email, password },
  });
  expect(login.ok()).toBeTruthy();
  const csrf = await request.get("/api/v1/auth/csrf");
  expect(csrf.ok()).toBeTruthy();
  const body = (await csrf.json()) as { token: string };
  return body.token;
}

export async function uiLogin(page: Page, email: string, password: string) {
  await page.goto("/login");
  await page.getByTestId("login-email").fill(email);
  await page.getByTestId("login-password").fill(password);
  await page.getByTestId("login-submit").click();
  await expect(page.getByTestId("current-user")).toBeVisible();
}

/** Upload tray meta is `done · {size} · 100%` (size may vary). */
export async function waitForUploadDone(page: Page, options?: { timeout?: number }) {
  await expect(page.getByText(/done · .+ · 100%/).first()).toBeVisible({
    timeout: options?.timeout ?? 60_000,
  });
}

/** Wait for upload tray, then for the file row (reload once if list refresh lags). */
export async function waitForUploadedRow(page: Page, rowTestId: string) {
  await waitForUploadDone(page);
  const row = page.getByTestId(rowTestId);
  try {
    await expect(row).toBeVisible({ timeout: 5_000 });
  } catch {
    await page.reload();
    await expect(row).toBeVisible({ timeout: 30_000 });
  }
}

/** Always create a fresh team so e2e is not poisoned by leftover folders/channels. */
export async function ensureFreshTeam(page: Page, namePrefix: string) {
  const stamp = String(Date.now());
  const name = `${namePrefix} ${stamp}`;
  const welcomeCreate = page.getByTestId("create-team");
  if (await welcomeCreate.isVisible().catch(() => false)) {
    await page.getByTestId("create-team-name").fill(name);
    await welcomeCreate.click();
  } else {
    await page.getByTestId("rail-create-team").click();
    await expect(page.getByTestId("create-team-dialog")).toBeVisible();
    await page.getByTestId("create-team-dialog-name").fill(name);
    await page.getByTestId("create-team-dialog-submit").click();
  }
  // Must wait for the *new* team URL — already being on /t/.../c/general matches too early.
  await expect(page).toHaveURL(new RegExp(`/t/[^/]*${stamp}[^/]*/c/general`), { timeout: 30_000 });
  const teamSlug = page.url().match(/\/t\/([^/]+)\//)?.[1];
  expect(teamSlug).toBeTruthy();
  return teamSlug!;
}

export async function openRowActions(page: Page, itemName: string) {
  await page.getByTestId(`actions-${itemName}`).click();
  await expect(page.getByTestId(`actions-menu-${itemName}`)).toBeVisible({ timeout: 5_000 });
}
