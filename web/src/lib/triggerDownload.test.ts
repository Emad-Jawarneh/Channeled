import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../api/http";
import { describeDownloadError, downloadItems, summarizeDownloadOutcome } from "./triggerDownload";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("downloadItems", () => {
  const blobSpy = vi.spyOn(Response.prototype, "blob");
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/auth/csrf")) return jsonResponse({ token: "csrf" });
      if (url.endsWith("/downloads/tickets")) {
        const body = JSON.parse(String(init?.body)) as { items: { itemId: string }[] };
        return jsonResponse({
          maxConcurrentDownloads: 4,
          results: body.items.map((i) =>
            i.itemId === "missing"
              ? { itemId: i.itemId, url: null, code: "not_found", message: "File not found." }
              : { itemId: i.itemId, url: `/api/v1/downloads/ticket-${i.itemId}` },
          ),
        });
      }
      throw new Error(`unexpected fetch ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    document.querySelectorAll("[data-testid='download-frame']").forEach((f) => f.remove());
    blobSpy.mockClear();
  });

  it("requests tickets with CSRF and starts staggered native downloads without reading bytes", async () => {
    const run = downloadItems([
      { scope: "channel", teamSlug: "t", channelName: "general", id: "a", name: "a.txt" },
      { scope: "personal", id: "b", name: "b.txt" },
    ]);
    // Past the 300 ms stagger, well before the 60 s frame cleanup.
    await vi.advanceTimersByTimeAsync(1_000);
    const outcome = await run;

    expect(outcome).toEqual({ started: 2, failures: [] });
    const ticketCall = fetchMock.mock.calls.find(([u]) => String(u).endsWith("/downloads/tickets"))!;
    expect(new Headers(ticketCall[1]?.headers).get("X-CSRF-TOKEN")).toBe("csrf");
    expect(JSON.parse(String(ticketCall[1]?.body))).toEqual({
      items: [
        { scope: "channel", itemId: "a", teamSlug: "t", channelName: "general" },
        { scope: "personal", itemId: "b" },
      ],
    });
    const frames = [...document.querySelectorAll<HTMLIFrameElement>("[data-testid='download-frame']")];
    expect(frames.map((f) => f.getAttribute("src"))).toEqual(["/api/v1/downloads/ticket-a", "/api/v1/downloads/ticket-b"]);
    expect(frames.every((f) => f.hidden)).toBe(true);
    expect(blobSpy).not.toHaveBeenCalled();
  });

  it("removes the hidden frames after a minute", async () => {
    const run = downloadItems([{ scope: "personal", id: "a", name: "a.txt" }]);
    await vi.runAllTimersAsync();
    await run;
    expect(document.querySelectorAll("[data-testid='download-frame']")).toHaveLength(0);
  });

  it("reports per-file failures and still starts the rest", async () => {
    const run = downloadItems([
      { scope: "personal", id: "missing", name: "gone.txt" },
      { scope: "personal", id: "ok", name: "ok.txt" },
    ]);
    await vi.runAllTimersAsync();
    const outcome = await run;
    expect(outcome.started).toBe(1);
    expect(outcome.failures).toEqual([
      { id: "missing", name: "gone.txt", message: "File not found, or you no longer have access." },
    ]);
    expect(summarizeDownloadOutcome(outcome)).toBe("gone.txt: File not found, or you no longer have access.");
  });

  it("turns an expired session into a sign-in message", () => {
    expect(describeDownloadError(new ApiError(401, "unauthorized", "Not authenticated."))).toBe(
      "Your session has expired. Sign in again to download.",
    );
  });
});
