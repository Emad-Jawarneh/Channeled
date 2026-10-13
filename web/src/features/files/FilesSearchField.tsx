import { useEffect, useId, useRef, useState } from "react";
import { TABLET_MQ, useMediaQuery } from "../../lib/useMediaQuery";
import styles from "./FileBrowser.module.css";

type Props = {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
};

function SearchGlyph() {
  return (
    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="11" cy="11" r="6.5" stroke="currentColor" strokeWidth="1.8" />
      <path d="M16.2 16.2 20 20" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  );
}

export function FilesSearchField({
  value,
  onChange,
  placeholder = "Filter by name",
}: Props) {
  const compact = useMediaQuery(TABLET_MQ);
  const [expanded, setExpanded] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const labelId = useId();
  const open = !compact || expanded || value.length > 0;

  useEffect(() => {
    if (!compact) setExpanded(false);
  }, [compact]);

  useEffect(() => {
    if (open && compact) {
      inputRef.current?.focus();
    }
  }, [open, compact]);

  if (!open) {
    return (
      <button
        type="button"
        className={styles.topSearchToggle}
        aria-label="Filter files"
        title="Filter files"
        data-testid="filter-name-toggle"
        onClick={() => setExpanded(true)}
      >
        <SearchGlyph />
      </button>
    );
  }

  return (
    <label className={styles.topSearch} data-expanded="true" htmlFor={labelId}>
      <span className={styles.searchIcon} aria-hidden="true">
        <SearchGlyph />
      </span>
      <span className="visually-hidden">Filter by name</span>
      <input
        id={labelId}
        ref={inputRef}
        className={styles.topSearchInput}
        data-testid="filter-name"
        type="search"
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onBlur={() => {
          if (compact && value.length === 0) setExpanded(false);
        }}
        onKeyDown={(e) => {
          if (e.key === "Escape" && compact) {
            if (value.length > 0) onChange("");
            setExpanded(false);
            (e.target as HTMLInputElement).blur();
          }
        }}
      />
    </label>
  );
}
