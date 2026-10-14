import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";
import { useLocation } from "react-router-dom";
import { filesApi, ItemKind, personalApi, type FileItem } from "../../api/channeled";
import { Button } from "../../ui/Button";
import { FileThumb } from "./FileThumb";
import {
  clearFileClipboard,
  clipboardLabel,
  clipboardSourceLabel,
  getFileClipboard,
  subscribeFileClipboard,
  type ClipboardItem,
  type ClipboardScope,
} from "./fileClipboard";
import { pasteTargetFromLocation } from "./pasteTarget";
import { pasteClipboardItems } from "./useFileClipboard";
import styles from "./ClipboardShelf.module.css";

function contentUrlFor(item: ClipboardItem, source: ClipboardScope): string | null {
  if (item.kind === ItemKind.Folder) return null;
  if (source.scope === "personal") return personalApi.contentUrl(item.id);
  return filesApi.contentUrl(source.teamSlug, source.channelName, item.id);
}

function asFileItem(item: ClipboardItem): FileItem {
  return {
    id: item.id,
    name: item.name,
    kind: item.kind,
    contentType: item.contentType ?? null,
    createdAt: "",
    updatedAt: "",
  };
}

export function ClipboardShelf() {
  const location = useLocation();
  const qc = useQueryClient();
  const clip = useSyncExternalStore(subscribeFileClipboard, getFileClipboard, () => null);
  const [open, setOpen] = useState(false);
  const [checked, setChecked] = useState<Set<string>>(() => new Set());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [summary, setSummary] = useState<string | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);

  const target = useMemo(
    () => pasteTargetFromLocation(location.pathname, location.search),
    [location.pathname, location.search],
  );

  useEffect(() => {
    if (!clip) {
      setChecked(new Set());
      setOpen(false);
      return;
    }
    setChecked(new Set(clip.items.map((i) => i.id)));
  }, [clip]);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: MouseEvent) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  if (!clip) return null;

  const ids = clip.items.map((i) => i.id);
  const selectedIds = ids.filter((id) => checked.has(id));
  const allSelected = ids.length > 0 && selectedIds.length === ids.length;
  const noneSelected = selectedIds.length === 0;

  function selectAll() {
    setChecked(new Set(ids));
  }
  function selectNone() {
    setChecked(new Set());
  }
  function invert() {
    setChecked((prev) => {
      const next = new Set<string>();
      for (const id of ids) {
        if (!prev.has(id)) next.add(id);
      }
      return next;
    });
  }

  function toggle(id: string) {
    setChecked((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function onPaste() {
    if (!target || noneSelected || busy) return;
    setBusy(true);
    setError(null);
    setSummary(null);
    try {
      const outcome = await pasteClipboardItems({ itemIds: selectedIds, target });
      if (!outcome) return;
      const { res, mode } = outcome;
      setSummary(
        res.failed === 0
          ? `Pasted ${res.succeeded} item${res.succeeded === 1 ? "" : "s"}`
          : `Pasted ${res.succeeded} of ${res.succeeded + res.failed}`,
      );
      if (mode === "copy" && allSelected && res.failed === 0) {
        clearFileClipboard();
        setOpen(false);
      }
      void qc.invalidateQueries({ queryKey: ["items"] });
      void qc.invalidateQueries({ queryKey: ["personal"] });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Paste failed");
    } finally {
      setBusy(false);
    }
  }

  const targetLabel = target
    ? target.scope === "personal"
      ? "Personal files"
      : `#${target.channelName}`
    : null;

  return (
    <div className={styles.root} ref={rootRef}>
      <button
        type="button"
        className={styles.trigger}
        aria-label={`${clipboardLabel(clip)}. Open clipboard shelf.`}
        aria-haspopup="true"
        aria-expanded={open}
        data-testid="clipboard-shelf"
        onClick={() => {
          setError(null);
          setSummary(null);
          setOpen((v) => !v);
        }}
      >
        <svg
          viewBox="0 0 24 24"
          width="18"
          height="18"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <rect x="8" y="2" width="8" height="4" rx="1" />
          <path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2" />
        </svg>
        <span className={styles.badge} data-testid="clipboard-shelf-badge">
          {clip.items.length > 99 ? "99+" : clip.items.length}
        </span>
      </button>

      {open && (
        <div className={styles.panel} role="region" aria-label="Clipboard" data-testid="clipboard-shelf-panel">
          <div className={styles.panelHead}>
            <div>
              <div className={styles.panelTitle}>{clipboardLabel(clip)}</div>
              <div className={styles.panelMeta}>From {clipboardSourceLabel(clip.source)}</div>
            </div>
            <button
              type="button"
              className={styles.linkButton}
              data-testid="clipboard-shelf-clear"
              onClick={() => {
                clearFileClipboard();
                setOpen(false);
              }}
            >
              Clear
            </button>
          </div>

          <div className={styles.selectionRow} role="group" aria-label="Select clipboard items">
            <button type="button" className={styles.segmentBtn} data-testid="clipboard-select-all" onClick={selectAll}>
              All
            </button>
            <button type="button" className={styles.segmentBtn} data-testid="clipboard-select-none" onClick={selectNone}>
              None
            </button>
            <button type="button" className={styles.segmentBtn} data-testid="clipboard-select-invert" onClick={invert}>
              Invert
            </button>
          </div>

          <ul className={styles.list}>
            {clip.items.map((item) => (
              <li key={item.id}>
                <label className={styles.row} data-testid="clipboard-shelf-item">
                  <input
                    type="checkbox"
                    className={styles.check}
                    checked={checked.has(item.id)}
                    onChange={() => toggle(item.id)}
                    aria-label={`Select ${item.name}`}
                  />
                  <FileThumb item={asFileItem(item)} contentUrl={contentUrlFor(item, clip.source)} size="sm" />
                  <span className={styles.rowName}>{item.name}</span>
                </label>
              </li>
            ))}
          </ul>

          <div className={styles.footer}>
            {targetLabel ? (
              <p className={styles.hint}>
                Paste into <strong>{targetLabel}</strong>
                {target?.folderId ? " (this folder)" : ""}
              </p>
            ) : (
              <p className={styles.hint} data-testid="clipboard-shelf-no-target">
                Open a file channel or personal files to paste.
              </p>
            )}
            <Button
              type="button"
              variant="primary"
              className={styles.pasteBtn}
              data-testid="clipboard-shelf-paste"
              disabled={!target || noneSelected || busy}
              onClick={() => void onPaste()}
            >
              {busy
                ? "Pasting…"
                : noneSelected
                  ? "Paste"
                  : `Paste ${selectedIds.length} selected`}
            </Button>
            {error && (
              <p className={styles.error} role="alert">
                {error}
              </p>
            )}
            {summary && (
              <p className={styles.summary} role="status">
                {summary}
              </p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
