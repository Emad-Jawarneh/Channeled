import { useSyncExternalStore } from "react";
import styles from "./FileBrowser.module.css";
import {
  clearFileClipboard,
  clipboardLabel,
  getFileClipboard,
  subscribeFileClipboard,
} from "./fileClipboard";

type Props = {
  onPaste?: () => void;
  pasteDisabled?: boolean;
};

export function ClipboardChip({ onPaste, pasteDisabled = false }: Props) {
  const clip = useSyncExternalStore(subscribeFileClipboard, getFileClipboard, () => null);
  if (!clip) return null;
  return (
    <div className={styles.clipboardChip} data-testid="clipboard-chip" role="status">
      <span>{clipboardLabel(clip)}</span>
      {onPaste && (
        <button
          type="button"
          className={styles.clipboardChipAction}
          data-testid="clipboard-paste"
          disabled={pasteDisabled}
          onClick={() => onPaste()}
        >
          Paste
        </button>
      )}
      <button
        type="button"
        className={styles.clipboardChipClear}
        data-testid="clipboard-clear"
        aria-label="Clear clipboard"
        onClick={() => clearFileClipboard()}
      >
        Clear
      </button>
    </div>
  );
}
