import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import type { ChannelMessage, MessageDeletedEvent, PresenceChangedEvent } from "../api/channeled";

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

    conn.on("filesChanged", onFilesChanged);
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
      offReconnect();
      void conn.invoke("LeaveChannel", id).catch(() => undefined);
    };
  }, [channelId]);
}
