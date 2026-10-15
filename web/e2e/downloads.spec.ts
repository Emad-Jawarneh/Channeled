import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import type { Download, Page } from "@playwright/test";
import {
  Visibility,
  addMember,
  createChannel,
  createTeam,
  expect,
  expectAccessible,
  grantChannel,
  revokeChannel,
  test,
  uploadToChannel,
  type Person,
  type TeamRef,
} from "./fixtures";
import { openRowActions } from "./helpers";

const sha = (bytes: Buffer | string) => createHash("sha256").update(bytes).digest("hex");

let owner: Person;
let member: Person;
let team: TeamRef;
const unicodeName = "résumé 📄.txt";
const unicodeBody = "unicode body " + "é".repeat(50);
const htmlBody = "<html><body><script>window.pwned = true</script>not rendered</body></html>";
// The e2e server caps each download stream at 2 MiB/s, so this takes ~10 s to transfer.
const big = Buffer.alloc(20 * 1024 * 1024, 7);

async function savedSha(download: Download): Promise<string> {
  const path = await download.path();
  return sha(await readFile(path!));
}

async function downloadViaRowMenu(page: Page, name: string): Promise<Download> {
  await openRowActions(page, name);
  const pending = page.waitForEvent("download");
  await page.getByTestId(`download-${name}`).click();
  return pending;
}

test.describe("native downloads @xbrowser", () => {
  test.beforeAll(async ({ people }) => {
    test.setTimeout(180_000);
    owner = await people.signUp("Downloader");
    member = await people.signUp("DownloadMember");
    team = await createTeam(owner);
    await addMember(owner, team.slug, member);
    await uploadToChannel(owner, team.slug, "general", unicodeName, unicodeBody);
    await uploadToChannel(owner, team.slug, "general", "page.html", htmlBody, { contentType: "text/html" });
    await uploadToChannel(owner, team.slug, "general", "big.bin", big, { contentType: "application/octet-stream" });
    await createChannel(owner, team.slug, "vault", { visibility: Visibility.Restricted });
    await grantChannel(owner, team.slug, "vault", member.id, true);
    await uploadToChannel(owner, team.slug, "vault", "secret.txt", "secret");
  });

  test("row download saves the exact bytes under the Unicode name", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general`);
    const download = await downloadViaRowMenu(page, unicodeName);
    expect(download.suggestedFilename()).toBe(unicodeName);
    expect(await savedSha(download)).toBe(sha(unicodeBody));
  });

  test("HTML files download instead of rendering", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general`);
    const download = await downloadViaRowMenu(page, "page.html");
    expect(download.suggestedFilename()).toBe("page.html");
    expect(await savedSha(download)).toBe(sha(htmlBody));
    expect(await page.evaluate(() => (window as unknown as { pwned?: boolean }).pwned)).toBeUndefined();
  });

  test("bulk download starts one native download per selected file", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general`);
    await page.getByTestId(`check-${unicodeName}`).check();
    await page.getByTestId("check-page.html").check();
    const names: string[] = [];
    page.on("download", (d) => names.push(d.suggestedFilename()));
    await page.getByTestId("bulk-download").click();
    await expect.poll(() => names.length).toBe(2);
    expect(names.sort()).toEqual(["page.html", unicodeName].sort());
  });

  test("a large download starts right away and streams", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general`);
    await openRowActions(page, "big.bin");
    const pending = page.waitForEvent("download");
    const clicked = Date.now();
    await page.getByTestId("download-big.bin").click();
    const download = await pending;
    // The old blob path only showed the download after all 20 MB arrived (~10 s at 2 MiB/s).
    expect(Date.now() - clicked).toBeLessThan(2_000);
    expect(download.suggestedFilename()).toBe("big.bin");
    await download.cancel();
  });

  test("an expired session shows a sign-in message instead of failing silently", async ({ openAs }) => {
    const page = await openAs(owner);
    await page.goto(`/t/${team.slug}/c/general`);
    await expect(page.getByTestId(`row-${unicodeName}`)).toBeVisible();
    await page.context().clearCookies();
    await openRowActions(page, unicodeName);
    await page.getByTestId(`download-${unicodeName}`).click();
    await expect(page.getByTestId("browser-error")).toContainText("Your session has expired");
    await expectAccessible(page, "download error banner");
  });

  test("revoked access is reported per file", async ({ openAs }) => {
    const page = await openAs(member);
    await page.goto(`/t/${team.slug}/c/vault`);
    await expect(page.getByTestId("row-secret.txt")).toBeVisible();
    await revokeChannel(owner, team.slug, "vault", member.id);
    await openRowActions(page, "secret.txt");
    await page.getByTestId("download-secret.txt").click();
    await expect(page.getByTestId("browser-error")).toContainText("no longer have access");
    await grantChannel(owner, team.slug, "vault", member.id, true);
  });
});
