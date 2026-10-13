import styles from "./FileBrowser.module.css";

type Props = {
  disabled?: boolean;
  hasItems: boolean;
  onSelectAll: () => void;
  onSelectNone: () => void;
  onInvert: () => void;
};

export function SelectionControls({
  disabled = false,
  hasItems,
  onSelectAll,
  onSelectNone,
  onInvert,
}: Props) {
  if (!hasItems) return null;

  return (
    <div className={styles.controlBlock} data-testid="selection-controls">
      <span className={styles.controlLabel} id="selection-controls-label">
        Select
      </span>
      <div
        className={styles.segment}
        role="group"
        aria-labelledby="selection-controls-label"
      >
        <button
          type="button"
          className={styles.segmentBtn}
          disabled={disabled}
          data-testid="select-all-btn"
          onClick={onSelectAll}
        >
          All
        </button>
        <button
          type="button"
          className={styles.segmentBtn}
          disabled={disabled}
          data-testid="select-none-btn"
          onClick={onSelectNone}
        >
          None
        </button>
        <button
          type="button"
          className={styles.segmentBtn}
          disabled={disabled}
          data-testid="select-invert-btn"
          onClick={onInvert}
        >
          Invert
        </button>
      </div>
    </div>
  );
}
