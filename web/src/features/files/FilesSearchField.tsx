import styles from "./FileBrowser.module.css";

type Props = {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
};

export function FilesSearchField({
  value,
  onChange,
  placeholder = "Filter by name",
}: Props) {
  return (
    <label className={styles.topSearch}>
      <span className={styles.searchIcon} aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none">
          <circle cx="11" cy="11" r="6.5" stroke="currentColor" strokeWidth="1.8" />
          <path d="M16.2 16.2 20 20" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
        </svg>
      </span>
      <span className="visually-hidden">Filter by name</span>
      <input
        className={styles.topSearchInput}
        data-testid="filter-name"
        type="search"
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </label>
  );
}
