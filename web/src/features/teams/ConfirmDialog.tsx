import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import { Button } from "../../ui/Button";
import styles from "./CreateChannelDialog.module.css";

type Props = {
  open: boolean;
  title: string;
  children: ReactNode;
  confirmLabel: string;
  /** When set, the confirm button stays disabled until this text is typed (case-insensitive). */
  requireText?: string;
  requireTextLabel?: string;
  /** When true, requires a non-empty password; value is passed to onConfirm. */
  requirePassword?: boolean;
  passwordLabel?: string;
  busy?: boolean;
  error?: string | null;
  testId?: string;
  onClose: () => void;
  onConfirm: (password?: string) => void | Promise<void>;
};

/** Small modal used for destructive/side-effecting confirmations (open join, channel visibility). */
export function ConfirmDialog({
  open,
  title,
  children,
  confirmLabel,
  requireText,
  requireTextLabel,
  requirePassword = false,
  passwordLabel = "Your password",
  busy = false,
  error = null,
  testId = "confirm-dialog",
  onClose,
  onConfirm,
}: Props) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const [typed, setTyped] = useState("");
  const [password, setPassword] = useState("");

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      setTyped("");
      setPassword("");
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  const textOk = requireText === undefined || typed.trim().toLowerCase() === requireText.toLowerCase();
  const passwordOk = !requirePassword || password.length > 0;

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!textOk || !passwordOk || busy) return;
    await onConfirm(requirePassword ? password : undefined);
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
        <div className={styles.hint}>{children}</div>
        {requireText !== undefined && (
          <label className={styles.label}>
            {requireTextLabel ?? `Type "${requireText}" to confirm`}
            <input
              className={styles.input}
              data-testid={`${testId}-input`}
              value={typed}
              onChange={(e) => setTyped(e.target.value)}
              autoComplete="off"
              disabled={busy}
            />
          </label>
        )}
        {requirePassword && (
          <label className={styles.label}>
            {passwordLabel}
            <input
              className={styles.input}
              type="password"
              data-testid={`${testId}-password`}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              disabled={busy}
            />
          </label>
        )}
        {error && (
          <p className={styles.error} role="alert" data-testid={`${testId}-error`}>
            {error}
          </p>
        )}
        <div className={styles.actions}>
          <Button type="button" onClick={onClose} disabled={busy} data-testid={`${testId}-cancel`}>
            Cancel
          </Button>
          <Button
            type="submit"
            variant="primary"
            disabled={busy || !textOk || !passwordOk}
            data-testid={`${testId}-submit`}
          >
            {busy ? "Working…" : confirmLabel}
          </Button>
        </div>
      </form>
    </dialog>
  );
}
