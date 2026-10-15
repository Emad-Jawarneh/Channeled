import { useCallback, useEffect, useSyncExternalStore } from "react";
import { clipboardApi, ItemKind, type ClipboardPasteResponse, type FileItem } from "../../api/channeled";
import {
  clearFileClipboard,
  clipboardItemIds,
  getFileClipboard,
  isTextFieldTarget,
  pruneFileClipboard,
  removeClipboardItems,
  setFileClipboard,
  scopeToPayload,
  subscribeFileClipboard,
  type ClipboardItem,
  type ClipboardMode,
  type ClipboardScope,
} from "./fileClipboard";

function toClipboardItems(ids: string[], listed: FileItem[]): ClipboardItem[] {
  const byId = new Map(listed.map((i) => [i.id, i]));
  return ids.map((id) => {
    const hit = byId.get(id);
    return {
      id,
      name: hit?.name ?? id,
      kind: hit?.kind ?? ItemKind.File,
      contentType: hit?.contentType ?? null,
    };
  });
}

export async function pasteClipboardItems(opts: {
  itemIds: string[];
  target: ClipboardScope;
}): Promise<{ res: ClipboardPasteResponse; mode: ClipboardMode } | null> {
  const current = getFileClipboard();
  if (!current || opts.itemIds.length === 0) return null;

  if (
    current.mode === "cut" &&
    opts.target.scope === "personal" &&
    current.source.scope === "channel"
  ) {
    throw new Error("Cut is not available when pasting into personal files from a team.");
  }

  const res = await clipboardApi.paste({
    mode: current.mode,
    itemIds: opts.itemIds,
    source: scopeToPayload(current.source),
    target: scopeToPayload(opts.target),
  });

  if (current.mode === "cut") {
    const succeeded = new Set(
      res.results.filter((r) => r.status === "succeeded").map((r) => r.itemId),
    );
    removeClipboardItems(succeeded);
  } else if (res.failed === 0) {
    // Copy paste leaves sources; clear only when the whole selection succeeded and user pasted all.
    // Shelf may paste a subset — keep remaining clipboard entries on copy.
  }

  return { res, mode: current.mode };
}

export function useFileClipboard(opts: {
  currentSource: ClipboardScope;
  listedIds: string[];
  /** Full list rows so cut/copy can capture names for the shelf. */
  listedItems: FileItem[];
  selectedIds: string[];
  /** When false (query still loading), skip prune so a remount does not clear the clipboard. */
  listReady?: boolean;
  enabled?: boolean;
  onPasteResult?: (res: ClipboardPasteResponse, mode: ClipboardMode) => void;
  onError?: (message: string) => void;
  refresh?: () => void;
}) {
  const clip = useSyncExternalStore(subscribeFileClipboard, getFileClipboard, () => null);
  const enabled = opts.enabled !== false;
  const listReady = opts.listReady !== false;

  useEffect(() => {
    if (!enabled || !clip || !listReady) return;
    const sameSource =
      clip.source.scope === opts.currentSource.scope &&
      (clip.source.scope === "personal"
        ? clip.source.folderId === opts.currentSource.folderId
        : clip.source.scope === "channel" &&
          opts.currentSource.scope === "channel" &&
          clip.source.teamSlug === opts.currentSource.teamSlug &&
          clip.source.channelName === opts.currentSource.channelName &&
          clip.source.folderId === opts.currentSource.folderId);
    if (!sameSource) return;
    pruneFileClipboard(opts.listedIds);
  }, [enabled, clip, listReady, opts.currentSource, opts.listedIds.join("|")]);

  const cutOrCopy = useCallback(
    (mode: ClipboardMode, ids?: string[]) => {
      const itemIds = ids?.length ? ids : opts.selectedIds;
      if (itemIds.length === 0) return;
      setFileClipboard({
        mode,
        items: toClipboardItems(itemIds, opts.listedItems),
        source: opts.currentSource,
      });
    },
    [opts.currentSource, opts.selectedIds, opts.listedItems],
  );

  const paste = useCallback(
    async (ids?: string[]) => {
      const current = getFileClipboard();
      if (!current) return;
      const itemIds = ids?.length ? ids : clipboardItemIds(current);
      try {
        const outcome = await pasteClipboardItems({
          itemIds,
          target: opts.currentSource,
        });
        if (!outcome) return;
        if (current.mode === "copy" && itemIds.length >= current.items.length && outcome.res.failed === 0) {
          clearFileClipboard();
        }
        opts.onPasteResult?.(outcome.res, outcome.mode);
        opts.refresh?.();
      } catch (e) {
        opts.onError?.(e instanceof Error ? e.message : "Paste failed");
      }
    },
    [opts],
  );

  useEffect(() => {
    if (!enabled) return;
    const onKey = (e: KeyboardEvent) => {
      if (isTextFieldTarget(e.target)) return;
      const meta = e.metaKey || e.ctrlKey;
      if (!meta) return;
      const key = e.key.toLowerCase();
      if (key === "x") {
        e.preventDefault();
        cutOrCopy("cut");
      } else if (key === "c") {
        e.preventDefault();
        cutOrCopy("copy");
      } else if (key === "v") {
        e.preventDefault();
        void paste();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [enabled, cutOrCopy, paste]);

  const isCutId = useCallback(
    (id: string) => clip?.mode === "cut" && clip.items.some((i) => i.id === id),
    [clip],
  );

  return { clip, cutOrCopy, paste, clear: clearFileClipboard, isCutId };
}
