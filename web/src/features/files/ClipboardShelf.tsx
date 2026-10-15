import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";
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

const TUTORIAL_KEY = "channeled.clipboardTutorialSeen";

function readTutorialSeen(): boolean {
  try {
    return localStorage.getItem(TUTORIAL_KEY) === "1";
  } catch {
    return true;
  }
}

function markTutorialSeen() {
  try {
    localStorage.setItem(TUTORIAL_KEY, "1");
  } catch {
    /* private mode */
  }
}

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
  const [tutorialSeen, setTutorialSeen] = useState(readTutorialSeen);
  const rootRef = useRef<HTMLDivElement>(null);
  const prevClipRef = useRef(false);

  const target = useMemo(
    () => pasteTargetFromLocation(location.pathname, location.search),
    [location.pathname, location.search],
  );

  const showTutorial = Boolean(clip) && !tutorialSeen && !open;

  const dismissTutorial = useCallback(() => {
    markTutorialSeen();
    setTutorialSeen(true);
  }, []);

  useEffect(() => {
    if (!clip) {
      setChecked(new Set());
      setOpen(false);
      prevClipRef.current = false;
      return;
    }
    setChecked(new Set(clip.items.map((i) => i.id)));
    // First time items land on the clipboard, keep the coachmark visible until dismissed.
    if (!prevClipRef.current && !readTutorialSeen()) {
      setTutorialSeen(false);
    }
    prevClipRef.current = true;
  }, [clip]);

  useEffect(() => {
    if (!open && !showTutorial) return;
    function onPointerDown(event: MouseEvent) {
      if (!rootRef.current?.contains(event.target as Node)) {
        if (open) setOpen(false);
        if (showTutorial) dismissTutorial();
      }
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        if (open) setOpen(false);
        if (showTutorial) dismissTutorial();
      }
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open, showTutorial, dismissTutorial]);

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
      const failures = res.results.filter((r) => r.status !== "succeeded");
      setSummary(
        res.failed === 0
          ? `Pasted ${res.succeeded} item${res.succeeded === 1 ? "" : "s"}`
          : `Pasted ${res.succeeded} of ${res.succeeded + res.failed}`,
      );
      if (failures.length > 0) {
        setError(failures.map((f) => f.message ?? f.errorCode ?? "Failed").join(" · "));
      }
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
        data-tutorial={showTutorial ? "true" : undefined}
        aria-label={`${clipboardLabel(clip)}. Open clipboard shelf.`}
        aria-haspopup="true"
        aria-expanded={open}
        aria-describedby={showTutorial ? "clipboard-tutorial" : undefined}
        data-testid="clipboard-shelf"
        onClick={() => {
          setError(null);
          setSummary(null);
          if (showTutorial) dismissTutorial();
          setOpen((v) => !v);
        }}
      >
        <svg
          viewBox="0 0 24 24"
          width="18"
          height="18"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.8"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          {clip.mode === "cut" ? (
            <>
              <circle cx="6" cy="6" r="2.5" />
              <circle cx="6" cy="18" r="2.5" />
              <path d="M20 4 8.5 15.5" />
              <path d="m14.5 14.5 5.5 5.5" />
              <path d="M8.5 8.5 12 12" />
            </>
          ) : (
            <>
              <rect x="9" y="9" width="11" height="11" rx="2" />
              <path d="M5 15V5a2 2 0 0 1 2-2h10" />
            </>
          )}
        </svg>
        <span className={styles.badge} data-testid="clipboard-shelf-badge">
          {clip.items.length > 99 ? "99+" : clip.items.length}
        </span>
      </button>

      {showTutorial && (
        <div
          id="clipboard-tutorial"
          className={styles.tutorial}
          role="status"
          data-testid="clipboard-tutorial"
        >
          <p className={styles.tutorialTitle}>Your clipboard is here</p>
          <p className={styles.tutorialBody}>
            Copied and cut files show up in this icon. Open it to paste into any folder.
          </p>
          <button
            type="button"
            className={styles.tutorialBtn}
            data-testid="clipboard-tutorial-dismiss"
            onClick={dismissTutorial}
          >
            Got it
          </button>
        </div>
      )}

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
