import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { shareLinksApi, type ShareLinkCreated } from "../../api/channeled";
import { Button } from "../../ui/Button";
import styles from "./CreateFolderDialog.module.css";

type Props = {
  open: boolean;
  teamSlug: string;
  channelName: string;
  fileId: string | null;
  fileName: string;
  onClose: () => void;
};

/**
 * Minimal public-link dialog. The raw link is shown exactly once, right after creation; the list below only
 * ever shows metadata (the server never returns the token again).
 */
export function ShareLinkDialog({ open, teamSlug, channelName, fileId, fileName, onClose }: Props) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const qc = useQueryClient();
  const [days, setDays] = useState(7);
  const [maxDownloads, setMaxDownloads] = useState(3);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<ShareLinkCreated | null>(null);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      setCreated(null);
      setError(null);
      setCopied(false);
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  const listKey = ["share-links", teamSlug, channelName, fileId];
  const links = useQuery({
    queryKey: listKey,
    queryFn: () => shareLinksApi.list(teamSlug, channelName, fileId ?? undefined),
    enabled: open && !!fileId,
  });

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    if (!fileId || busy) return;
    setBusy(true);
    setError(null);
    try {
      const result = await shareLinksApi.create(teamSlug, channelName, fileId, {
        expiresInDays: days,
        maxDownloads,
      });
      setCreated(result);
      setCopied(false);
      void qc.invalidateQueries({ queryKey: listKey });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not create link.");
    } finally {
      setBusy(false);
    }
  }

  async function onRevoke(id: string) {
    setError(null);
    try {
      await shareLinksApi.revoke(teamSlug, id);
      void qc.invalidateQueries({ queryKey: listKey });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not revoke link.");
    }
  }

  const fullUrl = created ? `${window.location.origin}${created.path}` : "";

  return (
    <dialog
      ref={dialogRef}
      className={styles.dialog}
      aria-labelledby={titleId}
      data-testid="share-link-dialog"
      onClose={onClose}
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) onClose();
      }}
      onClick={(e) => {
        if (e.target === dialogRef.current && !busy) onClose();
      }}
    >
      <form className={styles.panel} style={{ width: "min(30rem, 100%)" }} onSubmit={(e) => void onCreate(e)}>
        <h2 id={titleId} className={styles.title}>
          Public link · {fileName}
        </h2>

        {created ? (
          <div data-testid="share-link-created">
            <p className={styles.label}>Copy this link now — it will not be shown again.</p>
            <input
              className={styles.input}
              style={{ width: "100%" }}
              readOnly
              value={fullUrl}
              data-testid="share-link-url"
              onFocus={(e) => e.currentTarget.select()}
            />
            <div className={styles.actions} style={{ marginTop: "var(--space-3)" }}>
              <Button
                type="button"
                data-testid="share-link-copy"
                onClick={() => {
                  void navigator.clipboard?.writeText(fullUrl);
                  setCopied(true);
                }}
              >
                {copied ? "Copied" : "Copy link"}
              </Button>
            </div>
          </div>
        ) : (
          <>
            <label className={styles.label}>
              Expires after (days)
              <input
                className={styles.input}
                type="number"
                min={1}
                max={30}
                value={days}
                data-testid="share-link-days"
                onChange={(e) => setDays(Number(e.target.value))}
                disabled={busy}
              />
            </label>
            <label className={styles.label}>
              Maximum downloads
              <input
                className={styles.input}
                type="number"
                min={1}
                value={maxDownloads}
                data-testid="share-link-max"
                onChange={(e) => setMaxDownloads(Number(e.target.value))}
                disabled={busy}
              />
            </label>
            <div className={styles.actions}>
              <Button type="submit" variant="primary" disabled={busy} data-testid="share-link-create">
                {busy ? "Creating…" : "Create link"}
              </Button>
            </div>
          </>
        )}

        {error && (
          <p className={styles.error} data-testid="share-link-error">
            {error}
          </p>
        )}

        {(links.data?.length ?? 0) > 0 && (
          <div data-testid="share-link-list">
            <p className={styles.label}>Existing links</p>
            <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: "var(--space-2)" }}>
              {links.data!.map((l) => (
                <li key={l.id} style={{ display: "flex", justifyContent: "space-between", gap: "var(--space-3)" }}>
                  <span>
                    {l.status} · {l.downloadCount}/{l.maxDownloads ?? "∞"} downloads · expires{" "}
                    {new Date(l.expiresAt).toLocaleDateString()}
                  </span>
                  {l.status === "active" && (
                    <Button type="button" data-testid={`share-link-revoke-${l.id}`} onClick={() => void onRevoke(l.id)}>
                      Revoke
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          </div>
        )}

        <div className={styles.actions}>
          <Button type="button" onClick={onClose} disabled={busy} data-testid="share-link-close">
            Close
          </Button>
        </div>
      </form>
    </dialog>
  );
}
