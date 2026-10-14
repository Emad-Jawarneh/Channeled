export type ClipboardMode = "cut" | "copy";

export type ClipboardScope =
  | {
      scope: "channel";
      teamSlug: string;
      channelName: string;
      folderId: string | null;
    }
  | {
      scope: "personal";
      folderId: string | null;
    };

export type ClipboardItem = {
  id: string;
  name: string;
  kind: number;
  contentType?: string | null;
};

export type FileClipboardState = {
  mode: ClipboardMode;
  items: ClipboardItem[];
  source: ClipboardScope;
} | null;

type Listener = () => void;

let state: FileClipboardState = null;
const listeners = new Set<Listener>();

export function getFileClipboard(): FileClipboardState {
  return state;
}

export function subscribeFileClipboard(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function emit() {
  for (const l of listeners) l();
}

function dedupeItems(items: ClipboardItem[]): ClipboardItem[] {
  const seen = new Set<string>();
  const out: ClipboardItem[] = [];
  for (const item of items) {
    if (seen.has(item.id)) continue;
    seen.add(item.id);
    out.push(item);
  }
  return out;
}

export function setFileClipboard(next: Exclude<FileClipboardState, null>) {
  state = {
    mode: next.mode,
    items: dedupeItems(next.items),
    source: next.source,
  };
  emit();
}

export function clearFileClipboard() {
  if (!state) return;
  state = null;
  emit();
}

/** Remove entries by id (e.g. after a partial paste of a cut). */
export function removeClipboardItems(ids: Iterable<string>) {
  if (!state) return;
  const drop = new Set(ids);
  const next = state.items.filter((i) => !drop.has(i.id));
  if (next.length === state.items.length) return;
  if (next.length === 0) {
    clearFileClipboard();
    return;
  }
  state = { ...state, items: next };
  emit();
}

/** Drop ids that vanished from the current listing; clear entirely if none remain. */
export function pruneFileClipboard(visibleIds: Iterable<string>) {
  if (!state) return;
  const visible = new Set(visibleIds);
  const next = state.items.filter((i) => visible.has(i.id));
  if (next.length === state.items.length) return;
  if (next.length === 0) {
    clearFileClipboard();
    return;
  }
  state = { ...state, items: next };
  emit();
}

export function clipboardItemIds(clip: FileClipboardState): string[] {
  return clip?.items.map((i) => i.id) ?? [];
}

export function clipboardLabel(clip: Exclude<FileClipboardState, null>): string {
  const n = clip.items.length;
  const noun = n === 1 ? "item" : "items";
  return `${n} ${noun} ${clip.mode === "cut" ? "cut" : "copied"}`;
}

export function clipboardSourceLabel(source: ClipboardScope): string {
  if (source.scope === "personal") return "Personal files";
  return `#${source.channelName}`;
}

export function isTextFieldTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  const tag = target.tagName;
  if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return true;
  if (target.isContentEditable) return true;
  return Boolean(target.closest("input, textarea, select, [contenteditable='true']"));
}

export function scopeToPayload(scope: ClipboardScope) {
  if (scope.scope === "personal") {
    return { scope: "personal" as const, folderId: scope.folderId };
  }
  return {
    scope: "channel" as const,
    teamSlug: scope.teamSlug,
    channelName: scope.channelName,
    folderId: scope.folderId,
  };
}
