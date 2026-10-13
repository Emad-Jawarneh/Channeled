import { Button } from "../../ui/Button";
import styles from "./FileBrowser.module.css";

type Props = {
  count: number;
  busy?: boolean;
  downloadEnabled?: boolean;
  moveEnabled?: boolean;
  onClear: () => void;
  onDownload?: () => void;
  onNewFolder: () => void;
  onMoveTo: () => void;
  onSendToChannel: () => void;
};

export function BulkBar({
  count,
  busy = false,
  downloadEnabled = false,
  moveEnabled = true,
  onClear,
  onDownload,
  onNewFolder,
  onMoveTo,
  onSendToChannel,
}: Props) {
  if (count === 0) return null;

  return (
    <div className={styles.bulkBar} data-testid="bulk-bar" role="toolbar" aria-label="Selection actions">
      <span className={styles.bulkCount} data-testid="bulk-count">
        {count} selected
      </span>
      <div className={styles.bulkActions}>
        {onDownload && (
          <Button
            type="button"
            disabled={busy || !downloadEnabled}
            data-testid="bulk-download"
            onClick={onDownload}
          >
            Download
          </Button>
        )}
        <Button type="button" disabled={busy} data-testid="bulk-new-folder" onClick={onNewFolder}>
          New folder
        </Button>
        <Button
          type="button"
          disabled={busy || !moveEnabled}
          data-testid="bulk-move-to"
          onClick={onMoveTo}
        >
          Move to…
        </Button>
        <Button type="button" disabled={busy} data-testid="bulk-send-channel" onClick={onSendToChannel}>
          Send to channel…
        </Button>
        <Button type="button" disabled={busy} data-testid="bulk-clear" onClick={onClear}>
          Clear
        </Button>
      </div>
    </div>
  );
}
