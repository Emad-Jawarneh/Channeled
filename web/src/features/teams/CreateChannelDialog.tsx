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

const KIND_OPTIONS = [
  {
    value: ChannelKind.File,
    label: "File",
    detail: "Uploads, folders, and downloads",
    testId: "create-channel-kind-file",
  },
  {
    value: ChannelKind.Text,
    label: "Text",
    detail: "Messages and conversation",
    testId: "create-channel-kind-text",
  },
] as const;

const VISIBILITY_OPTIONS = [
  {
    value: ChannelVisibility.Everyone,
    label: "Everyone",
    detail: "All team members can see it",
    testId: "create-channel-visibility-everyone",
  },
  {
    value: ChannelVisibility.Restricted,
    label: "Restricted",
    detail: "Only people you grant access",
    testId: "create-channel-visibility-restricted",
  },
] as const;

function FileKindIcon() {
  return (
    <svg className={styles.choiceIcon} viewBox="0 0 24 24" aria-hidden="true">
      <path
        fill="currentColor"
        d="M6.5 3.75A1.75 1.75 0 0 0 4.75 5.5v13c0 .966.784 1.75 1.75 1.75h11a1.75 1.75 0 0 0 1.75-1.75V9.06a1.75 1.75 0 0 0-.513-1.238L14.18 3.763A1.75 1.75 0 0 0 12.94 3.25H6.5Zm7.19 1.56L17.69 9H14.5a.75.75 0 0 1-.75-.75V5.31Z"
      />
    </svg>
  );
}

function TextKindIcon() {
  return (
    <svg className={styles.choiceIcon} viewBox="0 0 24 24" aria-hidden="true">
      <path
        fill="currentColor"
        d="M4.75 5.5A1.75 1.75 0 0 1 6.5 3.75h11a1.75 1.75 0 0 1 1.75 1.75v9.5A1.75 1.75 0 0 1 17.5 16.75H9.56l-3.22 2.58a.75.75 0 0 1-1.22-.58v-2.25A1.75 1.75 0 0 1 4.75 15V5.5Z"
      />
    </svg>
  );
}

export function CreateChannelDialog({ open, busy = false, error = null, onClose, onCreate }: Props) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const kindGroupId = useId();
  const visibilityGroupId = useId();
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
      <form className={`${styles.panel} ${styles.panelCreate}`} onSubmit={(e) => void onSubmit(e)}>
        <header className={styles.header}>
          <p className={styles.eyebrow}>Channel</p>
          <h2 id={titleId} className={styles.title}>
            New channel
          </h2>
          <p className={styles.lede}>Name it, pick a type, then decide who can see it.</p>
        </header>

        <div className={styles.body}>
          <label className={styles.field}>
            <span className={styles.fieldLabel}>Channel name</span>
            <span className={styles.nameField}>
              <span className={styles.hash} aria-hidden="true">
                #
              </span>
              <input
                ref={inputRef}
                className={styles.nameInput}
                name="channel-display-name"
                data-testid="create-channel-name"
                data-bwignore="true"
                data-1p-ignore="true"
                data-lpignore="true"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="design, ops, releases"
                autoComplete="off"
                required
                maxLength={80}
                disabled={busy}
              />
            </span>
          </label>

          <div className={styles.field}>
            <span className={styles.fieldLabel} id={kindGroupId}>
              Type
            </span>
            <div
              className={styles.choiceGrid}
              role="radiogroup"
              aria-labelledby={kindGroupId}
              data-testid="create-channel-kind"
            >
              {KIND_OPTIONS.map((option) => {
                const selected = kind === option.value;
                return (
                  <button
                    key={option.value}
                    type="button"
                    role="radio"
                    aria-checked={selected}
                    className={styles.choice}
                    data-selected={selected ? "true" : "false"}
                    data-testid={option.testId}
                    disabled={busy}
                    onClick={() => setKind(option.value)}
                  >
                    {option.value === ChannelKind.File ? <FileKindIcon /> : <TextKindIcon />}
                    <span className={styles.choiceCopy}>
                      <span className={styles.choiceLabel}>{option.label}</span>
                      <span className={styles.choiceDetail}>{option.detail}</span>
                    </span>
                  </button>
                );
              })}
            </div>
            <p className={styles.caption}>Type is permanent once the channel is created.</p>
          </div>

          <div className={styles.field}>
            <span className={styles.fieldLabel} id={visibilityGroupId}>
              Visibility
            </span>
            <div
              className={styles.choiceGrid}
              role="radiogroup"
              aria-labelledby={visibilityGroupId}
              data-testid="create-channel-visibility"
            >
              {VISIBILITY_OPTIONS.map((option) => {
                const selected = visibility === option.value;
                return (
                  <button
                    key={option.value}
                    type="button"
                    role="radio"
                    aria-checked={selected}
                    className={styles.choice}
                    data-selected={selected ? "true" : "false"}
                    data-testid={option.testId}
                    data-value={String(option.value)}
                    disabled={busy}
                    onClick={() => setVisibility(option.value)}
                  >
                    <span className={styles.choiceCopy}>
                      <span className={styles.choiceLabel}>{option.label}</span>
                      <span className={styles.choiceDetail}>{option.detail}</span>
                    </span>
                  </button>
                );
              })}
            </div>
          </div>

          {error && (
            <p className={styles.error} role="alert" data-testid="create-channel-error">
              {error}
            </p>
          )}
        </div>

        <div className={`${styles.actions} ${styles.actionsCreate}`}>
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
