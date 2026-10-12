import { useTheme } from "./ThemeContext";
import styles from "./ThemeToggle.module.css";

type Props = {
  className?: string;
  compact?: boolean;
};

export function ThemeToggle({ className, compact = false }: Props) {
  const { theme, toggleTheme } = useTheme();
  const dark = theme === "dark";

  return (
    <button
      type="button"
      className={[styles.toggle, className ?? ""].filter(Boolean).join(" ")}
      onClick={toggleTheme}
      aria-pressed={dark}
      aria-label={dark ? "Switch to light mode" : "Switch to dark mode"}
      data-testid="theme-toggle"
      title={dark ? "Light mode" : "Dark mode"}
    >
      <span className={styles.icon} aria-hidden="true">
        {dark ? (
          <svg viewBox="0 0 24 24" fill="none">
            <circle cx="12" cy="12" r="4.5" stroke="currentColor" strokeWidth="1.8" />
            <path
              d="M12 3v2.2M12 18.8V21M3 12h2.2M18.8 12H21M5.6 5.6l1.6 1.6M16.8 16.8l1.6 1.6M18.4 5.6l-1.6 1.6M7.2 16.8l-1.6 1.6"
              stroke="currentColor"
              strokeWidth="1.8"
              strokeLinecap="round"
            />
          </svg>
        ) : (
          <svg viewBox="0 0 24 24" fill="none">
            <path
              d="M18.5 14.2A7.2 7.2 0 0 1 9.8 5.5 6.4 6.4 0 1 0 18.5 14.2Z"
              stroke="currentColor"
              strokeWidth="1.8"
              strokeLinejoin="round"
            />
          </svg>
        )}
      </span>
      {!compact && <span className={styles.label}>{dark ? "Light" : "Dark"}</span>}
    </button>
  );
}
