import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { ChannelKind, ChannelVisibility } from "../../api/channeled";
import { Button } from "../../ui/Button";
import styles from "./CreateChannelDialog.module.css";

export type CreateChannelInput = {
  name: string;
  visibility: number;
  kind: number;
};

type Props = {
  open: boolean;
  busy?: boolean;
  error?: string | null;
  onClose: () => void;
  onCreate: (input: CreateChannelInput) => void | Promise<void>;
};

export function CreateChannelDialog({ open, busy = false, error = null, onClose, onCreate }: Props) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const [name, setName] = useState("");
  const [visibility, setVisibility] = useState<number>(ChannelVisibility.Everyone);
  const [kind, setKind] = useState<number>(ChannelKind.File);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open && !dialog.open) {
      setName("");
      setVisibility(ChannelVisibility.Everyone);
      setKind(ChannelKind.File);
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
    await onCreate({ name: trimmed, visibility, kind });
  }

  return (
    <dialog
      ref={dialogRef}
      className={styles.dialog}
      aria-labelledby={titleId}
      data-testid="create-channel-dialog"
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
          New channel
        </h2>
        <p className={styles.hint}>
          File channels hold uploads and folders. Text channels hold messages. The type cannot be changed later.
        </p>
        <label className={styles.label}>
          Channel name
          <input
            ref={inputRef}
            className={styles.input}
            name="channel-display-name"
            data-testid="create-channel-name"
            data-bwignore="true"
            data-1p-ignore="true"
            data-lpignore="true"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. design, ops, releases"
            autoComplete="off"
            required
            maxLength={80}
            disabled={busy}
          />
        </label>
        <label className={styles.label}>
          Type
          <select
            className={styles.input}
            data-testid="create-channel-kind"
            value={kind}
            onChange={(e) => setKind(Number(e.target.value))}
            disabled={busy}
          >
            <option value={String(ChannelKind.File)}>File channel</option>
            <option value={String(ChannelKind.Text)}>Text channel</option>
          </select>
        </label>
        <label className={styles.label}>
          Visibility
          <select
            className={styles.input}
            data-testid="create-channel-visibility"
            value={visibility}
            onChange={(e) => setVisibility(Number(e.target.value))}
            disabled={busy}
          >
            <option value={ChannelVisibility.Everyone}>Everyone</option>
            <option value={ChannelVisibility.Restricted}>Restricted</option>
          </select>
        </label>
        <p className={styles.hint}>
          {visibility === ChannelVisibility.Restricted
            ? "Restricted: team admins and members you grant access to can see this channel."
            : "Everyone: all team members can see this channel."}
        </p>
        {error && (
          <p className={styles.error} role="alert" data-testid="create-channel-error">
            {error}
          </p>
        )}
        <div className={styles.actions}>
          <Button type="button" onClick={onClose} disabled={busy} data-testid="create-channel-cancel">
            Cancel
          </Button>
          <Button
            type="submit"
            variant="primary"
            disabled={busy || !name.trim()}
            data-testid="create-channel-submit"
          >
            {busy ? "Creating…" : "Create channel"}
          </Button>
        </div>
      </form>
    </dialog>
  );
}
