import styles from "./DropOverlay.module.css";

type Props = {
  active: boolean;
  label?: string;
};

export function DropOverlay({ active, label = "Drop files to upload" }: Props) {
  if (!active) return null;
  return (
    <div className={styles.overlay} role="status" aria-live="polite" data-testid="drop-overlay">
      <div className={styles.card}>
        <p className={styles.title}>{label}</p>
      </div>
    </div>
  );
}
