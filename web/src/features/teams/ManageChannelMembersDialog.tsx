import { useDeferredValue, useEffect, useId, useMemo, useRef, useState, type FormEvent } from "react";
import type { TeamMember } from "../../api/channeled";
import { Button } from "../../ui/Button";
import styles from "./ManageChannelMembersDialog.module.css";

const EMPTY_QUERY_LIMIT = 80;
const SEARCH_LIMIT = 150;

export type ManageChannelMembersDialogProps = {
  open: boolean;
  channelName: string;
  candidates: TeamMember[];
  busy?: boolean;
  error?: string | null;
  onClose: () => void;
  onAdd: (input: { userIds: string[]; readOnly: boolean }) => void | Promise<void>;
};

function matchesQuery(member: TeamMember, query: string): boolean {
  const q = query.trim().toLowerCase();
  if (!q) return true;
  return (
    member.displayName.toLowerCase().includes(q) ||
    member.email.toLowerCase().includes(q) ||
    member.username.toLowerCase().includes(q)
  );
}

export function ManageChannelMembersDialog({
  open,
  channelName,
  candidates,
  busy = false,
  error = null,
  onClose,
  onAdd,
}: ManageChannelMembersDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const searchId = useId();
  const [query, setQuery] = useState("");
  const deferredQuery = useDeferredValue(query);
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [readOnly, setReadOnly] = useState(false);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open && !dialog.open) {
      setQuery("");
      setPicked(new Set());
      setReadOnly(false);
      dialog.showModal();
      requestAnimationFrame(() => searchRef.current?.focus());
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  const filtered = useMemo(() => {
    const q = deferredQuery.trim();
    const matched = candidates.filter((m) => matchesQuery(m, q));
    const limit = q ? SEARCH_LIMIT : EMPTY_QUERY_LIMIT;
    return {
      total: matched.length,
      shown: matched.slice(0, limit),
      querying: q.length > 0,
    };
  }, [candidates, deferredQuery]);

  const pickedMembers = useMemo(
    () => candidates.filter((m) => picked.has(m.userId)),
    [candidates, picked],
  );

  function toggle(userId: string, on: boolean) {
    setPicked((prev) => {
      const next = new Set(prev);
      if (on) next.add(userId);
      else next.delete(userId);
      return next;
    });
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (busy || picked.size === 0) return;
    await onAdd({ userIds: [...picked], readOnly });
  }

  const listMeta =
    candidates.length === 0
      ? "Everyone eligible already has access."
      : filtered.querying
        ? filtered.total === 0
          ? "No matches"
          : filtered.total > filtered.shown.length
            ? `Showing ${filtered.shown.length} of ${filtered.total} matches`
            : `${filtered.total} match${filtered.total === 1 ? "" : "es"}`
        : filtered.total > filtered.shown.length
          ? `Showing ${filtered.shown.length} of ${filtered.total} · search to find others`
          : `${filtered.total} people available`;

  return (
    <dialog
      ref={dialogRef}
      className={styles.dialog}
      aria-labelledby={titleId}
      data-testid={`grant-add-${channelName}`}
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
          <div className={styles.headerCopy}>
            <p className={styles.eyebrow}>Restricted channel</p>
            <h2 id={titleId} className={styles.title}>
              Add members to #{channelName}
            </h2>
            <p className={styles.lede}>
              Search the team, select people, then add them. Admins already have access and are not listed.
            </p>
          </div>
          <Button
            type="button"
            className={styles.close}
            onClick={onClose}
            disabled={busy}
            data-testid={`grant-add-close-${channelName}`}
            aria-label="Close"
          >
            Close
          </Button>
        </header>

        <div className={styles.toolbar}>
          <label className={styles.searchLabel} htmlFor={searchId}>
            <span className={styles.searchCaption}>Search people</span>
            <input
              ref={searchRef}
              id={searchId}
              className={styles.search}
              type="search"
              data-testid={`grant-search-${channelName}`}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Filter by name or email"
              autoComplete="off"
              disabled={busy}
            />
          </label>
          <p className={styles.listMeta}>{listMeta}</p>
        </div>

        <div className={styles.main}>
          <aside className={styles.side} data-testid={`grant-selected-${channelName}`}>
            <div className={styles.sideHead}>
              <p className={styles.sideTitle}>Selected</p>
              <span className={styles.sideCount}>{pickedMembers.length}</span>
            </div>
            {pickedMembers.length === 0 ? (
              <p className={styles.sideEmpty}>Pick people from the list. They stay here while you keep searching.</p>
            ) : (
              <ul className={styles.chips}>
                {pickedMembers.map((m) => (
                  <li key={m.userId}>
                    <button
                      type="button"
                      className={styles.chip}
                      disabled={busy}
                      onClick={() => toggle(m.userId, false)}
                      aria-label={`Remove ${m.displayName}`}
                    >
                      <span className={styles.chipCopy}>
                        <span className={styles.chipName}>{m.displayName}</span>
                        <span className={styles.chipEmail}>{m.email}</span>
                      </span>
                      <span className={styles.chipRemove} aria-hidden="true">
                        ×
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </aside>

          <section className={styles.listPane} aria-label="Team members">
            <ul className={styles.list} role="listbox" aria-multiselectable="true">
              {filtered.shown.length === 0 ? (
                <li className={styles.listEmpty}>
                  {candidates.length === 0 ? "No one left to add." : "No people match that search."}
                </li>
              ) : (
                filtered.shown.map((m) => {
                  const checked = picked.has(m.userId);
                  return (
                    <li key={m.userId}>
                      <label className={styles.row} data-selected={checked ? "true" : "false"}>
                        <span className={styles.avatar} aria-hidden="true">
                          {(m.displayName.trim()[0] ?? "?").toUpperCase()}
                        </span>
                        <span className={styles.rowCopy}>
                          <span className={styles.rowName}>{m.displayName}</span>
                          <span className={styles.rowEmail}>{m.email}</span>
                        </span>
                        <input
                          type="checkbox"
                          className={styles.checkbox}
                          data-testid={`grant-pick-${channelName}-${m.email}`}
                          checked={checked}
                          disabled={busy}
                          onChange={(e) => toggle(m.userId, e.target.checked)}
                        />
                      </label>
                    </li>
                  );
                })
              )}
            </ul>
          </section>
        </div>

        <footer className={styles.footer}>
          <label className={styles.readOnly}>
            <input
              type="checkbox"
              data-testid={`grant-bulk-readonly-${channelName}`}
              checked={readOnly}
              disabled={busy || picked.size === 0}
              onChange={(e) => setReadOnly(e.target.checked)}
            />
            <span>
              <strong>Add as read-only</strong>
              <span className={styles.readOnlyHint}>They can view but not upload or post</span>
            </span>
          </label>

          <div className={styles.footerEnd}>
            {error && (
              <p className={styles.error} role="alert">
                {error}
              </p>
            )}
            <div className={styles.actions}>
              <Button
                type="button"
                onClick={onClose}
                disabled={busy}
                data-testid={`grant-add-cancel-${channelName}`}
              >
                Cancel
              </Button>
              <Button
                type="submit"
                variant="primary"
                disabled={busy || picked.size === 0}
                data-testid={`grant-add-submit-${channelName}`}
              >
                {busy
                  ? "Adding…"
                  : picked.size === 0
                    ? "Add selected"
                    : `Add ${picked.size} selected`}
              </Button>
            </div>
          </div>
        </footer>
      </form>
    </dialog>
  );
}
