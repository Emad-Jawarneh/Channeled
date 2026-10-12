import styles from "./UploadButton.module.css";

type Props = {
  testId: string;
  label?: string;
  onFiles: (files: File[]) => void;
};

export function UploadButton({ testId, label = "Upload files", onFiles }: Props) {
  return (
    <label className={styles.upload}>
      <span className={styles.icon} aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none">
          <path
            d="M12 16V5M12 5l-4 4M12 5l4 4"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M5 16.5V18a2.5 2.5 0 0 0 2.5 2.5h9A2.5 2.5 0 0 0 19 18v-1.5"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
          />
        </svg>
      </span>
      <span className={styles.label}>{label}</span>
      <input
        className={styles.input}
        data-testid={testId}
        type="file"
        multiple
        onChange={(e) => {
          const files = [...(e.target.files ?? [])];
          if (files.length) onFiles(files);
          e.target.value = "";
        }}
      />
    </label>
  );
}
