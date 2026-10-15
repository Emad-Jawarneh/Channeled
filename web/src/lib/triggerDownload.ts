import { api, ApiError } from "../api/http";

/** A file to download from a channel or from the caller's personal space. */
export type DownloadRequestItem =
  | { scope: "channel"; teamSlug: string; channelName: string; id: string; name: string }
  | { scope: "personal"; id: string; name: string };

export type DownloadFailure = { id: string; name: string; message: string };

/** `started` downloads were handed to the browser; `failures` explain the rest per file. */
export type DownloadOutcome = { started: number; failures: DownloadFailure[] };

type TicketResult = { itemId: string; url?: string | null; code?: string | null; message?: string | null };
type TicketResponse = { results: TicketResult[]; maxConcurrentDownloads: number };

const STAGGER_MS = 300;
const FRAME_TTL_MS = 60_000;

function failureMessage(code: string | null | undefined, message: string | null | undefined): string {
  if (code === "not_found") return "File not found, or you no longer have access.";
  if (code === "too_many_downloads") return message ?? "Too many downloads are running at once.";
  return message ?? "Download failed.";
}

/** User-facing text for a ticket request that failed as a whole (e.g. the session expired). */
export function describeDownloadError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return "Your session has expired. Sign in again to download.";
    if (error.status === 403 || error.status === 404) return "You no longer have access to this file.";
    return error.message;
  }
  return error instanceof Error ? error.message : "Download failed.";
}

/** Hands a ticket URL to the browser: a hidden frame navigation downloads natively (progress, resume). */
function startNativeDownload(url: string) {
  const frame = document.createElement("iframe");
  frame.hidden = true;
  frame.tabIndex = -1;
  frame.setAttribute("aria-hidden", "true");
  frame.dataset.testid = "download-frame";
  frame.src = url;
  document.body.appendChild(frame);
  window.setTimeout(() => frame.remove(), FRAME_TTL_MS);
}

/**
 * Asks the API for short-lived download tickets (errors surface here, while the app is in control), then
 * starts each download natively. Never reads file bytes into JavaScript.
 */
export async function downloadItems(items: DownloadRequestItem[]): Promise<DownloadOutcome> {
  if (items.length === 0) return { started: 0, failures: [] };
  const response = await api<TicketResponse>("/api/v1/downloads/tickets", {
    method: "POST",
    json: {
      items: items.map((item) =>
        item.scope === "channel"
          ? { scope: "channel", itemId: item.id, teamSlug: item.teamSlug, channelName: item.channelName }
          : { scope: "personal", itemId: item.id },
      ),
    },
  });

  const byId = new Map(items.map((item) => [item.id, item]));
  const failures: DownloadFailure[] = [];
  let started = 0;
  for (const result of response.results) {
    const item = byId.get(result.itemId);
    const name = item?.name ?? "File";
    if (!result.url) {
      failures.push({ id: result.itemId, name, message: failureMessage(result.code, result.message) });
      continue;
    }
    if (started > 0) {
      // Browsers coalesce or block several downloads started in the same tick.
      await new Promise((resolve) => window.setTimeout(resolve, STAGGER_MS));
    }
    startNativeDownload(result.url);
    started++;
  }
  return { started, failures };
}

/** One-line summary for a toast/error banner, or null when everything started. */
export function summarizeDownloadOutcome(outcome: DownloadOutcome): string | null {
  if (outcome.failures.length === 0) return null;
  if (outcome.failures.length === 1) {
    const only = outcome.failures[0]!;
    return `${only.name}: ${only.message}`;
  }
  const first = outcome.failures[0]!;
  return `${outcome.failures.length} files were not downloaded (${first.name}: ${first.message}).`;
}
