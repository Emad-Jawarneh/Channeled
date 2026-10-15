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

/**
 * Link that opens the file wherever it lives now (`/open/:id` locates it, then shows its folder or trash with the
 * row highlighted, even past the first page). Null when the viewer cannot open it.
 */
export function hrefForResolvedFile(file: ResolvedFile): string | null {
  if (!file.accessible || file.state === "missing") return null;
  return `/open/${encodeURIComponent(file.fileId)}`;
}
