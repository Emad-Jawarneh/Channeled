import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Button } from "../../ui/Button";
import styles from "./CreateFolderDialog.module.css";

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
  onClose: () => void;
  onPick: (id: string | null) => void | Promise<void>;
};

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
  onClose,
  onPick,
}: Props) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const [value, setValue] = useState<string>("");

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open && !dialog.open) {
      setValue(allowNone ? "" : (options[0]?.id ?? ""));
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open, allowNone, options]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    if (!allowNone && !value) return;
    await onPick(value === "" ? null : value);
  }

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
        <h2 id={titleId} className={styles.title}>
          {title}
        </h2>
        {options.length === 0 && !allowNone ? (
          <p className={styles.error}>{emptyLabel}</p>
        ) : (
          <label className={styles.label}>
            Destination
            <select
              className={styles.input}
              data-testid={`${testId}-select`}
              value={value}
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
          </label>
        )}
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
            disabled={busy || (options.length === 0 && !allowNone)}
            data-testid={`${testId}-submit`}
          >
            {busy ? "Working…" : confirmLabel}
          </Button>
        </div>
      </form>
    </dialog>
  );
}
