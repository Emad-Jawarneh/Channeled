import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { ChannelKind, type Channel } from "../../api/channeled";
import { Button } from "../../ui/Button";
import dialogStyles from "./CreateChannelDialog.module.css";
import styles from "./DeleteChannelDialog.module.css";

export type DeleteChannelDisposition =
  | { mode: "discard" }
  | { mode: "move"; targetChannel: string }
  | { mode: "none" };

type Props = {
  open: boolean;
  channel: Channel | null;
  otherFileChannels: Channel[];
  busy?: boolean;
  error?: string | null;
  onClose: () => void;
  onDelete: (disposition: DeleteChannelDisposition) => void | Promise<void>;
  onArchive: () => void | Promise<void>;
};

export function DeleteChannelDialog({
  open,
  channel,
  otherFileChannels,
  busy = false,
  error = null,
  onClose,
  onDelete,
  onArchive,
}: Props) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const [confirmName, setConfirmName] = useState("");
  const [disposition, setDisposition] = useState<"discard" | "move">("discard");
  const [moveTarget, setMoveTarget] = useState("");

  const isFile = channel != null && (channel.kind ?? ChannelKind.File) === ChannelKind.File;
  const nameMatches = channel != null && confirmName.trim() === channel.name;
  const moveReady = disposition !== "move" || (moveTarget !== "" && moveTarget !== channel?.name);
  const canConfirm = nameMatches && (!isFile || moveReady) && !busy;

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open && channel && !dialog.open) {
      setConfirmName("");
      setDisposition("discard");
      setMoveTarget(otherFileChannels[0]?.name ?? "");
      dialog.showModal();
      requestAnimationFrame(() => {
        dialog.querySelector<HTMLButtonElement>('[data-testid="delete-channel-cancel"]')?.focus();
      });
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open, channel, otherFileChannels]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!channel || !canConfirm) return;

    if (!isFile) {
      await onDelete({ mode: "none" });
      return;
    }

    if (disposition === "move") {
      await onDelete({ mode: "move", targetChannel: moveTarget });
      return;
    }

    await onDelete({ mode: "discard" });
  }

  if (!channel) return null;

  return (
    <dialog
      ref={dialogRef}
      className={dialogStyles.dialog}
      role="alertdialog"
      aria-labelledby={titleId}
      aria-modal="true"
      data-testid="delete-channel-dialog"
      onClose={onClose}
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) onClose();
      }}
      onClick={(e) => {
        if (e.target === dialogRef.current && !busy) onClose();
      }}
    >
      <form className={`${dialogStyles.panel} ${styles.form}`} onSubmit={(e) => void onSubmit(e)}>
        <h2 id={titleId} className={dialogStyles.title}>
          Delete #{channel.name}?
        </h2>
        <p className={styles.lede}>
          Permanent: removes all files and messages in this channel. This cannot be undone.
          {isFile && (
            <>
              {" "}
              Channels with more than <strong>10,000</strong> files cannot be deleted in one step.
            </>
          )}
        </p>

        {isFile && (
          <fieldset className={styles.fieldset} data-testid="delete-channel-disposition">
            <legend className={styles.legend}>What should happen to files?</legend>
            <label className={styles.option}>
              <input
                type="radio"
                name="disposition"
                value="discard"
                checked={disposition === "discard"}
                disabled={busy}
                onChange={() => setDisposition("discard")}
                data-testid="delete-channel-discard"
              />
              <span>Discard all files (delete from storage)</span>
            </label>
            <label className={styles.option}>
              <input
                type="radio"
                name="disposition"
                value="move"
                checked={disposition === "move"}
                disabled={busy || otherFileChannels.length === 0}
                onChange={() => setDisposition("move")}
                data-testid="delete-channel-move"
              />
              <span>Move files to another channel</span>
            </label>
            {disposition === "move" && (
              <label className={styles.confirmLabel}>
                Destination
                <select
                  className={`${dialogStyles.input} ${styles.select}`}
                  value={moveTarget}
                  disabled={busy || otherFileChannels.length === 0}
                  onChange={(e) => setMoveTarget(e.target.value)}
                  data-testid="delete-channel-move-target"
                >
                  {otherFileChannels.length === 0 && <option value="">No other file channels</option>}
                  {otherFileChannels.map((c) => (
                    <option key={c.id} value={c.name}>
                      #{c.name}
                    </option>
                  ))}
                </select>
              </label>
            )}
          </fieldset>
        )}

        <label className={styles.confirmLabel}>
          Type <strong>{channel.name}</strong> to confirm
          <input
            className={dialogStyles.input}
            value={confirmName}
            onChange={(e) => setConfirmName(e.target.value)}
            autoComplete="off"
            disabled={busy}
            data-testid="delete-channel-confirm-name"
          />
        </label>

        {error && (
          <p className={styles.error} role="alert" aria-live="assertive" data-testid="delete-channel-error">
            {error}
          </p>
        )}

        <div className={styles.actions}>
          <Button type="button" onClick={onClose} disabled={busy} data-testid="delete-channel-cancel">
            Cancel
          </Button>
          <Button type="button" disabled={busy} onClick={() => void onArchive()} data-testid="delete-channel-archive">
            Archive instead
          </Button>
          <Button
            type="submit"
            variant="primary"
            disabled={!canConfirm}
            data-testid="delete-channel-submit"
          >
            {busy ? "Deleting…" : "Delete channel"}
          </Button>
        </div>
      </form>
    </dialog>
  );
}
