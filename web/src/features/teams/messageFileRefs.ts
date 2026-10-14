import type { ResolvedFile } from "../../api/channeled";

export const MAX_MESSAGE_FILE_REFS = 5;

/** De-dupe while preserving first-seen order; drops empty ids. */
export function normalizeComposerFileIds(ids: string[]): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const id of ids) {
    const trimmed = id.trim();
    if (!trimmed || seen.has(trimmed)) continue;
    seen.add(trimmed);
    out.push(trimmed);
  }
  return out;
}

/** Append ids up to the max; returns ids that did not fit. */
export function addComposerFileIds(current: string[], toAdd: string[]): { next: string[]; rejected: string[] } {
  const merged = normalizeComposerFileIds([...current, ...toAdd]);
  if (merged.length <= MAX_MESSAGE_FILE_REFS) {
    return { next: merged, rejected: [] };
  }
  return {
    next: merged.slice(0, MAX_MESSAGE_FILE_REFS),
    rejected: merged.slice(MAX_MESSAGE_FILE_REFS),
  };
}

export function labelForResolvedFile(file: ResolvedFile): string {
  if (!file.accessible) return "A file you don't have access to";
  if (file.state === "missing") return "File no longer available";
  if (file.state === "trashed") return file.name ? `${file.name} (in trash)` : "File in trash";
  return file.name ?? "File";
}

/** Path to open the file in its channel (folder + highlight). Null when not navigable. */
export function hrefForResolvedFile(teamSlug: string, file: ResolvedFile): string | null {
  if (!file.accessible || !file.channelName || file.state === "missing") return null;
  const channel = encodeURIComponent(file.channelName);
  const base =
    file.state === "trashed"
      ? `/t/${encodeURIComponent(teamSlug)}/c/${channel}/trash`
      : `/t/${encodeURIComponent(teamSlug)}/c/${channel}`;
  const q = new URLSearchParams();
  if (file.state !== "trashed" && file.folderId) q.set("folder", file.folderId);
  q.set("item", file.fileId);
  const qs = q.toString();
  return qs ? `${base}?${qs}` : base;
}
