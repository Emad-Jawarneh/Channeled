import styles from "./PreviewToggle.module.css";

type Props = {
  open: boolean;
  onToggle: () => void;
};

export function PreviewToggle({ open, onToggle }: Props) {
  const label = open ? "Hide preview" : "Show preview";
  return (
    <button
      type="button"
      className={styles.toggle}
      data-testid="toggle-preview"
      aria-pressed={open}
      aria-label={label}
      title={label}
      onClick={onToggle}
    >
      <span className={styles.icon} aria-hidden="true">
        {open ? (
          <svg viewBox="0 0 24 24" fill="none">
            <rect x="3.5" y="4.5" width="17" height="15" rx="2" stroke="currentColor" strokeWidth="1.8" />
            <path d="M14.5 4.5v15" stroke="currentColor" strokeWidth="1.8" />
            <path
              d="M17 10.5 19.5 12 17 13.5"
              stroke="currentColor"
              strokeWidth="1.8"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
        ) : (
          <svg viewBox="0 0 24 24" fill="none">
            <rect x="3.5" y="4.5" width="17" height="15" rx="2" stroke="currentColor" strokeWidth="1.8" />
            <path d="M14.5 4.5v15" stroke="currentColor" strokeWidth="1.8" />
          </svg>
        )}
      </span>
    </button>
  );
}
