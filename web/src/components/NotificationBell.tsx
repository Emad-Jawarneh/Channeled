import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { NotificationType, notificationsApi, type NotificationDto } from "../api/channeled";
import { useAuth } from "../auth/AuthContext";
import { useNotifications } from "../realtime/useChannelHub";
import styles from "./NotificationBell.module.css";

const COUNT_KEY = ["notifications", "unread-count"] as const;
const LIST_KEY = ["notifications", "list"] as const;

function describe(n: NotificationDto): string {
  if (n.type === NotificationType.TeamJoinRequest) {
    return n.actorDisplayName
      ? `${n.actorDisplayName} requested to join ${n.teamSlug}`
      : `New join request for ${n.teamSlug}`;
  }
  return n.type === NotificationType.Reply
    ? `${n.actorDisplayName} replied to you in #${n.channelName}`
    : `${n.actorDisplayName} mentioned you in #${n.channelName}`;
}

function targetFor(n: NotificationDto): string {
  return n.type === NotificationType.TeamJoinRequest
    ? `/t/${n.teamSlug}/settings/general`
    : `/t/${n.teamSlug}/c/${n.channelName}`;
}

function formatWhen(iso: string): string {
  const date = new Date(iso);
  const diffMs = Date.now() - date.getTime();
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  return date.toLocaleDateString([], { month: "short", day: "numeric" });
}

export function NotificationBell() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const enabled = !!user;

  const count = useQuery({
    queryKey: COUNT_KEY,
    queryFn: () => notificationsApi.unreadCount(),
    enabled,
  });
  const list = useQuery({
    queryKey: LIST_KEY,
    queryFn: () => notificationsApi.list({ limit: 50 }),
    enabled: enabled && open,
  });

  function refresh() {
    void qc.invalidateQueries({ queryKey: ["notifications"] });
  }

  useNotifications(enabled, {
    onNotification: () => refresh(),
    onResync: () => refresh(),
  });

  const markRead = useMutation({
    mutationFn: (id: string) => notificationsApi.markRead(id),
    onSettled: refresh,
  });
  const markAllRead = useMutation({
    mutationFn: () => notificationsApi.markAllRead(),
    onSettled: refresh,
  });

  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  if (!user) return null;

  const unread = count.data?.unreadCount ?? 0;
  const items = list.data ?? [];

  function onSelect(n: NotificationDto) {
    if (!n.readAt) markRead.mutate(n.id);
    setOpen(false);
    navigate(targetFor(n));
  }

  return (
    <div className={styles.root} ref={rootRef}>
      <button
        type="button"
        className={styles.bell}
        onClick={() => {
          if (!open) refresh();
          setOpen((v) => !v);
        }}
        aria-label={`Notifications` + (unread > 0 ? ` (${unread} unread)` : "")}
        aria-haspopup="true"
        aria-expanded={open}
        data-testid="notification-bell"
      >
        <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9" />
          <path d="M10.3 21a1.94 1.94 0 0 0 3.4 0" />
        </svg>
        {unread > 0 && (
          <span className={styles.badge} data-testid="notification-badge">
            {unread > 99 ? "99+" : unread}
          </span>
        )}
      </button>

      {open && (
        <div className={styles.panel} role="region" aria-label="Notifications" data-testid="notification-panel">
          <div className={styles.panelHead}>
            <span className={styles.panelTitle}>Notifications</span>
            <button
              type="button"
              className={styles.linkButton}
              onClick={() => markAllRead.mutate()}
              disabled={unread === 0 || markAllRead.isPending}
              data-testid="notification-read-all"
            >
              Mark all read
            </button>
          </div>

          {list.isLoading && <p className={styles.empty}>Loading…</p>}
          {list.isError && (
            <p className={styles.empty} role="alert">
              Could not load notifications.
            </p>
          )}
          {list.isSuccess && items.length === 0 && (
            <p className={styles.empty} data-testid="notification-empty">
              You're all caught up.
            </p>
          )}

          <ul className={styles.list}>
            {items.map((n) => (
              <li key={n.id}>
                <button
                  type="button"
                  className={`${styles.item} ${n.readAt ? "" : styles.unread}`}
                  onClick={() => onSelect(n)}
                  data-testid="notification-item"
                >
                  <span className={styles.dot} aria-hidden="true" />
                  <span className={styles.itemText}>
                    <span className={styles.itemTitle}>{describe(n)}</span>
                    {n.bodyPreview && <span className={styles.itemPreview}>{n.bodyPreview}</span>}
                    <span className={styles.itemMeta}>
                      {n.teamSlug} · {formatWhen(n.createdAt)}
                    </span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
