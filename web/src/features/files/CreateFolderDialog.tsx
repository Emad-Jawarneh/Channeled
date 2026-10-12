import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Button } from "../../ui/Button";
import styles from "./CreateFolderDialog.module.css";

type Props = {
  open: boolean;
  busy?: boolean;
  error?: string | null;
  onClose: () => void;
  onCreate: (name: string) => void | Promise<void>;
};

export function CreateFolderDialog({ open, busy = false, error = null, onClose, onCreate }: Props) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const [name, setName] = useState("");

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open && !dialog.open) {
      setName("");
      dialog.showModal();
      requestAnimationFrame(() => inputRef.current?.focus());
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const trimmed = name.trim();
    if (!trimmed || busy) return;
    await onCreate(trimmed);
  }

  return (
    <dialog
      ref={dialogRef}
      className={styles.dialog}
      aria-labelledby={titleId}
      data-testid="create-folder-dialog"
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
          New folder
        </h2>
        <label className={styles.label}>
          Folder name
          <input
            ref={inputRef}
            className={styles.input}
            data-testid="create-folder-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            autoComplete="off"
            required
            maxLength={255}
            disabled={busy}
          />
        </label>
        {error && (
          <p className={styles.error} data-testid="create-folder-error">
            {error}
          </p>
        )}
        <div className={styles.actions}>
          <Button type="button" onClick={onClose} disabled={busy} data-testid="create-folder-cancel">
            Cancel
          </Button>
          <Button type="submit" variant="primary" disabled={busy || !name.trim()} data-testid="create-folder-submit">
            {busy ? "Creating…" : "Create"}
          </Button>
        </div>
      </form>
    </dialog>
  );
}
