import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://127.0.0.1:5173";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  // Specs seed isolated users/teams through e2e/fixtures.ts; serial specs opt in per file.
  workers: process.env.PW_WORKERS ? Number(process.env.PW_WORKERS) : 2,
  timeout: 180_000,
  expect: {
    timeout: 15_000,
    toHaveScreenshot: { maxDiffPixelRatio: 0.01, animations: "disabled", caret: "hide" },
  },
  // Baselines are created on first run and only replaced with --update-snapshots.
  updateSnapshots: "missing",
  snapshotPathTemplate: "{testDir}/__screenshots__/{testFilePath}/{arg}-{projectName}{ext}",
  reporter: [["list"]],
  use: {
    baseURL,
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    reducedMotion: "reduce",
  },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    // Firefox binds Ctrl+K to its own search bar and handles downloads differently.
    { name: "firefox", use: { ...devices["Desktop Firefox"] }, grep: /@xbrowser/ },
  ],
});
