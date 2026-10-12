import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Button } from "../../ui/Button";
import styles from "./CreateTeamDialog.module.css";

type Props = {
  open: boolean;
  busy?: boolean;
  error?: string | null;
  onClose: () => void;
  onCreate: (name: string) => void | Promise<void>;
};

export function CreateTeamDialog({ open, busy = false, error = null, onClose, onCreate }: Props) {
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
    if (trimmed.length < 2 || busy) return;
    await onCreate(trimmed);
  }

  return (
    <dialog
      ref={dialogRef}
      className={styles.dialog}
      aria-labelledby={titleId}
      data-testid="create-team-dialog"
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
          New team
        </h2>
        <p className={styles.hint}>
          You can create as many teams as you need. A <strong>general</strong> channel opens automatically.
        </p>
        <label className={styles.label}>
          Team name
          <input
            ref={inputRef}
            className={styles.input}
            name="team-display-name"
            data-testid="create-team-dialog-name"
            data-bwignore="true"
            data-1p-ignore="true"
            data-lpignore="true"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Product, Design, Ops"
            autoComplete="off"
            required
            minLength={2}
            maxLength={80}
            disabled={busy}
          />
        </label>
        {error && (
          <p className={styles.error} role="alert" data-testid="create-team-dialog-error">
            {error}
          </p>
        )}
        <div className={styles.actions}>
          <Button type="button" onClick={onClose} disabled={busy} data-testid="create-team-dialog-cancel">
            Cancel
          </Button>
          <Button
            type="submit"
            variant="primary"
            disabled={busy || name.trim().length < 2}
            data-testid="create-team-dialog-submit"
          >
            {busy ? "Creating…" : "Create team"}
          </Button>
        </div>
      </form>
    </dialog>
  );
}
