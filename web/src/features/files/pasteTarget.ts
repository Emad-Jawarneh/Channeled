import type { ClipboardScope } from "./fileClipboard";

const GUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Resolve the paste destination from the current URL.
 * Returns null when not on a writable file browser view (chat, settings, trash, etc.).
 */
export function pasteTargetFromLocation(pathname: string, search: string): ClipboardScope | null {
  const params = new URLSearchParams(search);
  const rawFolder = params.get("folder");
  const folderId = rawFolder && GUID_RE.test(rawFolder) ? rawFolder : null;

  if (pathname === "/me/files" || pathname.startsWith("/me/files/")) {
    // Only the main personal browser accepts paste (not recent/starred/trash).
    if (pathname !== "/me/files") return null;
    return { scope: "personal", folderId };
  }

  const match = /^\/t\/([^/]+)\/c\/([^/]+)\/?$/.exec(pathname);
  if (!match) return null;
  const teamSlug = decodeURIComponent(match[1]!);
  const channelName = decodeURIComponent(match[2]!);
  return { scope: "channel", teamSlug, channelName, folderId };
}
