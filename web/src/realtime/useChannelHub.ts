import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import type {
  ChannelMessage,
  MessageDeletedEvent,
  NotificationDto,
  PresenceChangedEvent,
} from "../api/channeled";

type FilesChangedPayload = { channelId: string };

/**
 * One shared hub connection per tab. The server only accepts JoinChannel/LeaveChannel from clients;
 * everything else (messages, deletes) is REST, and the hub pushes the committed result.
 */
let shared: HubConnection | null = null;
let starting: Promise<void> | null = null;
const reconnectListeners = new Set<() => void>();

function getConnection(): HubConnection {
  if (shared) {
    return shared;
  }

  const conn = new HubConnectionBuilder()
    .withUrl("/hubs/channel", {
      withCredentials: true,
      skipNegotiation: true,
      transport: HttpTransportType.WebSockets,
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  // Groups are per-connection on the server, so every subscriber must re-join after a reconnect.
  conn.onreconnected(() => {
    reconnectListeners.forEach((listener) => listener());
  });

  shared = conn;
  return conn;
}

function ensureStarted(conn: HubConnection): Promise<void> {
  if (conn.state === HubConnectionState.Connected) {
    return Promise.resolve();
  }
  if (conn.state !== HubConnectionState.Disconnected) {
    // Connecting/Reconnecting: the reconnect listener re-joins once the transport is back.
    return starting ?? Promise.reject(new Error("hub not ready"));
  }
  starting = conn.start().finally(() => {
    starting = null;
  });
  return starting;
}

function onReconnected(listener: () => void): () => void {
  reconnectListeners.add(listener);
  return () => {
    reconnectListeners.delete(listener);
  };
}

export type NotificationHandlers = {
  /** Hub `notificationCreated` payload (pushed to the signed-in user's own group; no join needed). */
  onNotification: (notification: NotificationDto) => void;
  /** Called after a reconnect: events may have been missed, so refetch the count/list. */
  onResync?: () => void;
};

/** Subscribes to per-user notification pushes on the shared hub connection. */
export function useNotifications(enabled: boolean, handlers: NotificationHandlers) {
  const handlersRef = useRef(handlers);
  handlersRef.current = handlers;

  useEffect(() => {
    if (!enabled) {
      return;
    }
    const conn = getConnection();
    let cancelled = false;

    const created = (notification: NotificationDto) => {
      handlersRef.current.onNotification(notification);
    };
    conn.on("notificationCreated", created);
    const offReconnect = onReconnected(() => {
      handlersRef.current.onResync?.();
    });

    void ensureStarted(conn)
      .then(() => {
        // Anything pushed before the socket was up is not replayed.
        if (!cancelled) {
          handlersRef.current.onResync?.();
        }
      })
      .catch(() => {
        /* reconnect will retry; REST polling on mount still gives a count */
      });

    return () => {
      cancelled = true;
      conn.off("notificationCreated", created);
      offReconnect();
    };
  }, [enabled]);
}

export function useChannelFilesChanged(channelId: string | undefined, teamSlug: string, channelName: string) {
  const qc = useQueryClient();
  const channelRef = useRef(channelId);
  channelRef.current = channelId;

  useEffect(() => {
    if (!channelId) {
      return;
    }
    // Channel.id is a string (Guid); narrow once so closures keep a definite `string`.
    const id: string = channelId;
    const conn = getConnection();
    let cancelled = false;

    const onFilesChanged = (payload: FilesChangedPayload) => {
      if (payload.channelId !== channelRef.current) {
        return;
      }
      void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] });
    };

    const rejoin = () => {
      void conn.invoke("JoinChannel", id).then(() => {
        void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] });
      });
    };

    // The server evicts every connection from the group when visibility/grants change. Members who still
    // have access re-join (which re-authorizes); everyone else is refused and just refetches.
    const onVisibilityChanged = (payload: FilesChangedPayload) => {
      if (payload.channelId !== channelRef.current) {
        return;
      }
      void qc.invalidateQueries({ queryKey: ["channels", teamSlug] });
      void qc.invalidateQueries({ queryKey: ["channel-members", teamSlug, channelName] });
      void conn
        .invoke("JoinChannel", id)
        .then(() => qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] }))
        .catch(() => qc.invalidateQueries({ queryKey: ["channels", teamSlug] }));
    };

    conn.on("filesChanged", onFilesChanged);
    conn.on("channelVisibilityChanged", onVisibilityChanged);
    const offReconnect = onReconnected(rejoin);

    void (async () => {
      await ensureStarted(conn);
      if (cancelled) {
        return;
      }
      await conn.invoke("JoinChannel", id);
    })().catch(() => {
      /* reconnect will retry */
    });

    return () => {
      cancelled = true;
      conn.off("filesChanged", onFilesChanged);
      conn.off("channelVisibilityChanged", onVisibilityChanged);
      offReconnect();
      void conn.invoke("LeaveChannel", id).catch(() => undefined);
    };
  }, [channelId, teamSlug, channelName, qc]);
}

export type ChannelChatHandlers = {
  onMessageCreated: (message: ChannelMessage) => void;
  onMessageDeleted: (event: MessageDeletedEvent) => void;
  onPresenceChanged: (event: PresenceChangedEvent) => void;
  /**
   * Called after every (re)join. The caller must refetch the latest page (and presence) and reconcile:
   * events can be missed while disconnected, and the server never replays them.
   */
  onResync: () => void;
};

export function useChannelChat(channelId: string | undefined, handlers: ChannelChatHandlers) {
  const qc = useQueryClient();
  const handlersRef = useRef(handlers);
  handlersRef.current = handlers;

  useEffect(() => {
    if (!channelId) {
      return;
    }
    const id: string = channelId;
    const conn = getConnection();
    let cancelled = false;

    const created = (message: ChannelMessage) => {
      if (message.channelId === id) {
        handlersRef.current.onMessageCreated(message);
      }
    };
    const deleted = (event: MessageDeletedEvent) => {
      if (event.channelId === id) {
        handlersRef.current.onMessageDeleted(event);
      }
    };
    const presence = (event: PresenceChangedEvent) => {
      if (event.channelId === id) {
        handlersRef.current.onPresenceChanged(event);
      }
    };

    conn.on("messageCreated", created);
    conn.on("messageDeleted", deleted);
    conn.on("presenceChanged", presence);

    const join = async () => {
      await conn.invoke("JoinChannel", id);
      if (!cancelled) {
        handlersRef.current.onResync();
      }
    };

    // Visibility/grants changed: the server evicted the group. Re-join (re-authorizes) and resync;
    // if access was lost the join is refused, so refetch the channel list instead.
    const visibilityChanged = (event: FilesChangedPayload) => {
      if (event.channelId !== id) {
        return;
      }
      void qc.invalidateQueries({ queryKey: ["channels"] });
      void join().catch(() => {
        void qc.invalidateQueries({ queryKey: ["channels"] });
      });
    };
    conn.on("channelVisibilityChanged", visibilityChanged);

    const offReconnect = onReconnected(() => {
      void join().catch(() => undefined);
    });

    void (async () => {
      try {
        await ensureStarted(conn);
        if (cancelled) {
          return;
        }
        await join();
      } catch {
        // Hub unavailable: still load history over REST; a reconnect re-joins and resyncs.
        if (!cancelled) {
          handlersRef.current.onResync();
        }
      }
    })();

    return () => {
      cancelled = true;
      conn.off("messageCreated", created);
      conn.off("messageDeleted", deleted);
      conn.off("presenceChanged", presence);
      conn.off("channelVisibilityChanged", visibilityChanged);
      offReconnect();
      void conn.invoke("LeaveChannel", id).catch(() => undefined);
    };
  }, [channelId, qc]);
}
