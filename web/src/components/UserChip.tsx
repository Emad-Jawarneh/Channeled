import { useEffect, useId, useRef, useState } from "react";
import { accountApi, type UserCard } from "../api/channeled";
import styles from "./UserChip.module.css";

type Props = {
  userId: string;
  displayName: string;
  username?: string;
  avatarVersion?: number | null;
  /** When the viewer cannot open a card (former/deleted). */
  status?: "ok" | "former" | "deleted";
};

export function UserChip({ userId, displayName, username, avatarVersion, status = "ok" }: Props) {
  const [open, setOpen] = useState(false);
  const [card, setCard] = useState<UserCard | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const popoverId = useId();

  useEffect(() => {
    if (!open) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") {
        setOpen(false);
        triggerRef.current?.focus();
      }
    }
    function onPointer(e: MouseEvent) {
      if (!rootRef.current?.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    window.addEventListener("keydown", onKey);
    window.addEventListener("mousedown", onPointer);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("mousedown", onPointer);
    };
  }, [open]);

  useEffect(() => {
    if (!open || status !== "ok") {
      return;
    }

    let cancelled = false;
    void accountApi
      .getUserCard(userId)
      .then((c) => {
        if (!cancelled) setCard(c);
      })
      .catch(() => {
        if (!cancelled) setCard(null);
      });
    return () => {
      cancelled = true;
    };
  }, [open, status, userId]);

  const label =
    status === "former" ? "Former member" : status === "deleted" ? "Deleted user" : displayName;
  const avatarUrl =
    status === "ok" && avatarVersion
      ? `/api/v1/users/${userId}/avatar?v=${avatarVersion}`
      : null;

  return (
    <div className={styles.root} ref={rootRef}>
      <button
        ref={triggerRef}
        type="button"
        className={styles.chip}
        aria-expanded={open}
        aria-controls={popoverId}
        onClick={() => setOpen((v) => !v)}
      >
        {avatarUrl ? <img src={avatarUrl} alt="" className={styles.avatar} /> : <span className={styles.initials}>{label.slice(0, 1)}</span>}
        <span>{label}</span>
      </button>
      {open && (
        <div id={popoverId} className={styles.popover} role="dialog">
          <strong>{card?.displayName ?? label}</strong>
          {status === "ok" && (card?.username ?? username) && (
            <div className={styles.muted}>@{card?.username ?? username}</div>
          )}
          {status === "ok" && card?.bio && <p className={styles.bio}>{card.bio}</p>}
          {status !== "ok" && <div className={styles.muted}>No shared team profile</div>}
        </div>
      )}
    </div>
  );
}
