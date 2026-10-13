import { useDeferredValue, useEffect, useId, useMemo, useRef, useState, type FormEvent } from "react";
import { Button } from "../../ui/Button";
import styles from "./PickListDialog.module.css";

export type PickOption = { id: string; label: string };

type Props = {
  open: boolean;
  title: string;
  testId: string;
  options: PickOption[];
  busy?: boolean;
  error?: string | null;
  confirmLabel?: string;
  emptyLabel?: string;
  allowNone?: boolean;
  noneLabel?: string;
  description?: string;
  onClose: () => void;
  onPick: (id: string | null) => void | Promise<void>;
};

type Row = { id: string; label: string };

export function PickListDialog({
  open,
  title,
  testId,
  options,
  busy = false,
  error = null,
  confirmLabel = "Confirm",
  emptyLabel = "Nothing available",
  allowNone = false,
  noneLabel = "Root",
  description = "Search and choose a destination, then confirm.",
  onClose,
  onPick,
}: Props) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const searchId = useId();
  const [value, setValue] = useState<string>("");
  const [query, setQuery] = useState("");
  const deferredQuery = useDeferredValue(query);

  const rows = useMemo<Row[]>(() => {
    const list: Row[] = allowNone ? [{ id: "", label: noneLabel }, ...options] : [...options];
    const q = deferredQuery.trim().toLowerCase();
    if (!q) return list;
    return list.filter((row) => row.label.toLowerCase().includes(q));
  }, [allowNone, noneLabel, options, deferredQuery]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open && !dialog.open) {
      setQuery("");
      setValue(allowNone ? "" : (options[0]?.id ?? ""));
      dialog.showModal();
      requestAnimationFrame(() => searchRef.current?.focus());
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open, allowNone, options]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    if (!allowNone && !value) return;
    if (options.length === 0 && !allowNone) return;
    await onPick(value === "" ? null : value);
  }

  const hasChoices = options.length > 0 || allowNone;

  return (
    <dialog
      ref={dialogRef}
      className={styles.dialog}
      aria-labelledby={titleId}
      data-testid={testId}
      onClose={onClose}
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) onClose();
      }}
      onClick={(e) => {
        if (e.target === dialogRef.current && !busy) onClose();
      }}
    >
      <form className={styles.panel} onSubmit={(e) => void onSubmit(e)}>
        <header className={styles.header}>
          <h2 id={titleId} className={styles.title}>
            {title}
          </h2>
          <p className={styles.lede}>{description}</p>
        </header>

        {hasChoices ? (
          <>
            <div className={styles.toolbar}>
              <label className={styles.searchLabel} htmlFor={searchId}>
                Search
                <input
                  ref={searchRef}
                  id={searchId}
                  className={styles.search}
                  type="search"
                  data-testid={`${testId}-search`}
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="Filter destinations"
                  autoComplete="off"
                  disabled={busy}
                />
              </label>
              <p className={styles.listMeta}>
                {rows.length === 0
                  ? "No matches"
                  : `${rows.length} destination${rows.length === 1 ? "" : "s"}`}
              </p>
            </div>

            <div className={styles.listPane}>
              <ul className={styles.list} role="listbox" aria-label="Destination">
                {rows.length === 0 ? (
                  <li className={styles.listEmpty}>No destinations match that search.</li>
                ) : (
                  rows.map((row) => {
                    const selected = value === row.id;
                    return (
                      <li key={row.id === "" ? "__none__" : row.id}>
                        <button
                          type="button"
                          role="option"
                          aria-selected={selected}
                          className={styles.row}
                          data-selected={selected ? "true" : "false"}
                          data-testid={`${testId}-option-${row.label}`}
                          disabled={busy}
                          onClick={() => setValue(row.id)}
                          onDoubleClick={() => {
                            if (busy) return;
                            setValue(row.id);
                            void onPick(row.id === "" ? null : row.id);
                          }}
                        >
                          <span className={styles.rowLabel}>{row.label}</span>
                          <span className={styles.check} aria-hidden="true" />
                        </button>
                      </li>
                    );
                  })
                )}
              </ul>
            </div>

            <select
              className={styles.srSelect}
              data-testid={`${testId}-select`}
              value={value}
              tabIndex={-1}
              aria-hidden="true"
              onChange={(e) => setValue(e.target.value)}
              disabled={busy}
            >
              {allowNone && <option value="">{noneLabel}</option>}
              {options.map((opt) => (
                <option key={opt.id} value={opt.id}>
                  {opt.label}
                </option>
              ))}
            </select>
          </>
        ) : (
          <div className={styles.listPane}>
            <p className={styles.listEmpty} role="status">
              {emptyLabel}
            </p>
          </div>
        )}

        <footer className={styles.footer}>
          {error && (
            <p className={styles.error} role="alert">
              {error}
            </p>
          )}
          <div className={styles.actions}>
            <Button type="button" onClick={onClose} disabled={busy}>
              Cancel
            </Button>
            <Button
              type="submit"
              variant="primary"
              disabled={busy || !hasChoices || (!allowNone && !value)}
              data-testid={`${testId}-submit`}
            >
              {busy ? "Working…" : confirmLabel}
            </Button>
          </div>
        </footer>
      </form>
    </dialog>
  );
}
