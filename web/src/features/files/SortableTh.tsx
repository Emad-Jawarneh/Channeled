import styles from "./FileBrowser.module.css";

export type SortColumn = "name" | "type" | "size" | "updated";

type Props = {
  column: SortColumn;
  label: string;
  sort: SortColumn;
  sortDir: "asc" | "desc";
  onSort: (column: SortColumn) => void;
  testId?: string;
};

export function SortableTh({ column, label, sort, sortDir, onSort, testId }: Props) {
  const active = sort === column;
  return (
    <th
      scope="col"
      aria-sort={active ? (sortDir === "asc" ? "ascending" : "descending") : "none"}
    >
      <button
        type="button"
        className={styles.sortHead}
        data-active={active ? "true" : "false"}
        data-testid={testId ?? `sort-col-${column}`}
        onClick={() => onSort(column)}
      >
        {label}
        {active && (
          <span className={styles.sortHeadDir} aria-hidden="true">
            {sortDir === "asc" ? "↑" : "↓"}
          </span>
        )}
      </button>
    </th>
  );
}

/** Name / Type → ascending first; Size / Modified → descending first. */
export function nextSortState(
  current: SortColumn,
  currentDir: "asc" | "desc",
  column: SortColumn,
): { sort: SortColumn; sortDir: "asc" | "desc" } {
  if (current === column) {
    return { sort: column, sortDir: currentDir === "asc" ? "desc" : "asc" };
  }
  return {
    sort: column,
    sortDir: column === "name" || column === "type" ? "asc" : "desc",
  };
}
