import styles from "../features/files/FileBrowser.module.css";

export function ModifiedAt({ value }: { value: string }) {
  const d = new Date(value);
  return (
    <span className={styles.modified} data-testid="modified-at">
      <span>{d.toLocaleDateString()}</span>
      <span>{d.toLocaleTimeString()}</span>
    </span>
  );
}
