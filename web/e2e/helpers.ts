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
