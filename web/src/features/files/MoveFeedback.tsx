import { useEffect } from "react";
import { Button } from "../../ui/Button";
import type { MoveBatchFailure } from "./moveBatch";
import styles from "./MoveFeedback.module.css";

type ProgressProps = {
  done: number;
  total: number;
};

export function MoveProgress({ done, total }: ProgressProps) {
  if (total <= 0) return null;
  return (
    <p className={styles.progress} role="status" aria-live="polite" data-testid="move-progress">
      Moving {done} / {total}…
    </p>
  );
}

type SummaryProps = {
  message: string;
  failures?: MoveBatchFailure[];
};

export function MoveSummary({ message, failures = [] }: SummaryProps) {
  return (
    <div className={styles.summary} role="status" aria-live="polite" data-testid="move-summary">
      <p className={styles.summaryText}>{message}</p>
      {failures.length > 0 && (
        <ul className={styles.failures}>
          {failures.slice(0, 5).map((f) => (
            <li key={f.id}>
              {f.name ?? f.id}: {f.message}
            </li>
          ))}
          {failures.length > 5 && <li>…and {failures.length - 5} more</li>}
        </ul>
      )}
    </div>
  );
}

export type UndoToastState = {
  message: string;
  onUndo: () => void;
};

type ToastProps = {
  toast: UndoToastState | null;
  onDismiss: () => void;
  ttlMs?: number;
};

export function UndoToast({ toast, onDismiss, ttlMs = 8000 }: ToastProps) {
  useEffect(() => {
    if (!toast) return;
    const t = window.setTimeout(onDismiss, ttlMs);
    return () => window.clearTimeout(t);
  }, [toast, onDismiss, ttlMs]);

  if (!toast) return null;

  return (
    <div className={styles.toast} role="status" aria-live="polite" data-testid="undo-toast">
      <span>{toast.message}</span>
      <Button type="button" data-testid="undo-move" onClick={toast.onUndo}>
        Undo
      </Button>
      <button type="button" className={styles.dismiss} aria-label="Dismiss" onClick={onDismiss}>
        ×
      </button>
    </div>
  );
}
