import { useQuery } from "@tanstack/react-query";
import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useReducer,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent,
} from "react";
import {
  ChannelVisibility,
  MESSAGE_MAX_LENGTH,
  messagesApi,
  teamsApi,
  type PresenceUser,
} from "../../api/channeled";
import { ApiError } from "../../api/http";
import { useAuth } from "../../auth/AuthContext";
import { AppShell } from "../../components/AppShell";
import { useChannelChat } from "../../realtime/useChannelHub";
import { Button } from "../../ui/Button";
import { ChannelSidebar } from "./ChannelSidebar";
import {
  canDeleteMessage,
  canPostMessage,
  chatReducer,
  initialChatState,
  type ResyncBuffer,
} from "./chatState";
import local from "./TextChannelPage.module.css";

type Props = {
  teamSlug: string;
  channelName: string;
};

/** Counts Unicode code points, matching the server/database limit (not UTF-16 code units). */
function codePointLength(value: string): number {
  return Array.from(value).length;
}

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 429) return "You're sending messages too quickly. Try again in a moment.";
    if (error.status === 403) return "You don't have permission to do that in this channel.";
    if (error.status === 409) return "This channel is archived and read-only.";
    return error.message;
  }
  return error instanceof Error ? error.message : "Something went wrong.";
}

function formatTime(iso: string): string {
  const date = new Date(iso);
  return date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function formatDay(iso: string): string {
  return new Date(iso).toLocaleDateString([], { weekday: "short", month: "short", day: "numeric" });
}

export function TextChannelPage({ teamSlug, channelName }: Props) {
  const { user } = useAuth();
  const channels = useQuery({
    queryKey: ["channels", teamSlug],
    queryFn: () => teamsApi.channels(teamSlug),
  });
  const team = useQuery({
    queryKey: ["team", teamSlug],
    queryFn: () => teamsApi.get(teamSlug),
  });

  const channel = (channels.data ?? []).find((c) => c.name === channelName);
  const archived = Boolean(channel?.archivedAt);
  const teamRole = team.data?.myRole;

  const [state, dispatch] = useReducer(chatReducer, initialChatState);
  const [online, setOnline] = useState<PresenceUser[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const [sendError, setSendError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const listRef = useRef<HTMLDivElement>(null);
  const stickToBottom = useRef(true);
  const preserveScroll = useRef<{ height: number; top: number } | null>(null);
  const resyncBuffer = useRef<ResyncBuffer | null>(null);
  const resyncSeq = useRef(0);

  // Switching channels starts from a clean slate (no flash of the previous channel's messages).
  useEffect(() => {
    // Invalidate any in-flight resync from the previous channel.
    resyncSeq.current += 1;
    resyncBuffer.current = null;
    dispatch({ type: "reset" });
    setOnline([]);
    setLoadError(null);
    setSendError(null);
    setActionError(null);
    setDraft("");
    stickToBottom.current = true;
  }, [teamSlug, channelName]);

  /** Refetch the latest page (+ presence) and reconcile; events that land mid-fetch are buffered and replayed. */
  const resync = useCallback(async () => {
    const seq = ++resyncSeq.current;
    const buffer: ResyncBuffer = { created: [], deleted: new Set() };
    resyncBuffer.current = buffer;
    try {
      const [page, presence] = await Promise.all([
        messagesApi.list(teamSlug, channelName),
        messagesApi.presence(teamSlug, channelName).catch(() => null),
      ]);
      if (seq !== resyncSeq.current) {
        return;
      }
      dispatch({ type: "resync", page, buffer });
      if (presence) {
        setOnline(presence.users);
      }
      setLoadError(null);
    } catch (error) {
      if (seq === resyncSeq.current) {
        setLoadError(describeError(error));
      }
    } finally {
      if (seq === resyncSeq.current) {
        resyncBuffer.current = null;
      }
    }
  }, [teamSlug, channelName]);

  useChannelChat(channel?.id, {
    onMessageCreated: (message) => {
      resyncBuffer.current?.created.push(message);
      dispatch({ type: "created", message });
    },
    onMessageDeleted: ({ messageId }) => {
      resyncBuffer.current?.deleted.add(messageId);
      dispatch({ type: "deleted", messageId });
    },
    onPresenceChanged: (event) => {
      setOnline((current) => {
        const others = current.filter((u) => u.userId !== event.userId);
        return event.online ? [...others, { userId: event.userId, displayName: event.displayName }] : others;
      });
    },
    onResync: () => {
      void resync();
    },
  });

  // Keep the newest message in view unless the reader scrolled up; keep position when older pages prepend.
  useLayoutEffect(() => {
    const el = listRef.current;
    if (!el) return;
    if (preserveScroll.current) {
      el.scrollTop = el.scrollHeight - preserveScroll.current.height + preserveScroll.current.top;
      preserveScroll.current = null;
    } else if (stickToBottom.current) {
      el.scrollTop = el.scrollHeight;
    }
  }, [state.messages]);

  function onScroll() {
    const el = listRef.current;
    if (!el) return;
    stickToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 48;
  }

  async function loadOlder() {
    const cursor = state.olderCursor;
    if (!cursor || loadingOlder) return;
    setLoadingOlder(true);
    try {
      const page = await messagesApi.list(teamSlug, channelName, { cursor });
      const el = listRef.current;
      if (el) {
        preserveScroll.current = { height: el.scrollHeight, top: el.scrollTop };
      }
      dispatch({ type: "older", page });
    } catch (error) {
      setLoadError(describeError(error));
    } finally {
      setLoadingOlder(false);
    }
  }

  const length = codePointLength(draft);
  const tooLong = length > MESSAGE_MAX_LENGTH;
  const canPost = canPostMessage(teamRole, archived);
  const canSend = canPost && !sending && !tooLong && draft.trim().length > 0;

  async function send(event?: FormEvent) {
    event?.preventDefault();
    if (!canSend) return;
    setSending(true);
    setSendError(null);
    try {
      const message = await messagesApi.post(teamSlug, channelName, draft);
      stickToBottom.current = true;
      dispatch({ type: "created", message });
      setDraft("");
    } catch (error) {
      setSendError(describeError(error));
    } finally {
      setSending(false);
    }
  }

  function onComposerKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void send();
    }
  }

  async function remove(messageId: string) {
    setActionError(null);
    try {
      await messagesApi.remove(teamSlug, channelName, messageId);
      dispatch({ type: "deleted", messageId });
    } catch (error) {
      setActionError(describeError(error));
    }
  }

  const sortedOnline = [...online].sort((a, b) => a.displayName.localeCompare(b.displayName));
  let lastDay = "";

  return (
    <AppShell
      title={`#${channelName}`}
      sidebar={<ChannelSidebar teamSlug={teamSlug} channelName={channelName} />}
    >
      <section className={local.chat} aria-label={`Messages in ${channelName}`} data-testid="text-channel-home">
        <header className={local.chatHead}>
          <div>
            <h2 className={local.chatTitle}>#{channelName}</h2>
            <p className={local.chatMeta}>
              {channel?.visibility === ChannelVisibility.Private ? "Private" : "Open"} text channel
              {channel?.description?.trim() ? ` · ${channel.description}` : ""}
            </p>
          </div>
          <div className={local.presence} data-testid="chat-presence" aria-live="polite">
            <span className={local.presenceLabel}>Online now ({sortedOnline.length})</span>
            <ul className={local.presenceList}>
              {sortedOnline.map((u) => (
                <li key={u.userId} className={local.presenceItem}>
                  <span className={local.presenceDot} aria-hidden="true" />
                  {u.displayName}
                  {u.userId === user?.id ? " (you)" : ""}
                </li>
              ))}
            </ul>
          </div>
        </header>

        <div
          ref={listRef}
          className={local.messages}
          onScroll={onScroll}
          role="log"
          aria-live="polite"
          aria-label="Message history"
          data-testid="chat-messages"
        >
          {state.olderCursor && (
            <div className={local.olderRow}>
              <Button type="button" onClick={() => void loadOlder()} disabled={loadingOlder} data-testid="chat-load-older">
                {loadingOlder ? "Loading…" : "Load older messages"}
              </Button>
            </div>
          )}

          {!state.loaded && !loadError && <p className={local.status}>Loading messages…</p>}
          {loadError && (
            <p className={local.error} role="alert" data-testid="chat-load-error">
              {loadError}{" "}
              <Button type="button" onClick={() => void resync()}>
                Retry
              </Button>
            </p>
          )}
          {state.loaded && state.messages.length === 0 && !loadError && (
            <p className={local.status} data-testid="chat-empty">
              No messages yet. Say hello!
            </p>
          )}

          <ul className={local.messageList}>
            {state.messages.map((message) => {
              const day = formatDay(message.createdAt);
              const showDay = day !== lastDay;
              lastDay = day;
              const mine = message.authorId === user?.id;
              return (
                <li key={message.id} className={local.messageItem} data-testid="chat-message">
                  {showDay && <p className={local.day}>{day}</p>}
                  <div className={local.message}>
                    <div className={local.messageHead}>
                      <span className={local.author}>{message.authorDisplayName}</span>
                      <time className={local.time} dateTime={message.createdAt} title={new Date(message.createdAt).toLocaleString()}>
                        {formatTime(message.createdAt)}
                      </time>
                      {canDeleteMessage({ isAuthor: mine, teamRole }) && (
                        <button
                          type="button"
                          className={local.deleteButton}
                          onClick={() => void remove(message.id)}
                          aria-label={`Delete message from ${message.authorDisplayName} at ${formatTime(message.createdAt)}`}
                          data-testid="chat-delete"
                        >
                          Delete
                        </button>
                      )}
                    </div>
                    {/* Plain text only: React escapes it and pre-wrap keeps line breaks. */}
                    <p className={local.body}>{message.body}</p>
                  </div>
                </li>
              );
            })}
          </ul>
        </div>

        {actionError && (
          <p className={local.error} role="alert" data-testid="chat-action-error">
            {actionError}
          </p>
        )}

        <form className={local.composer} onSubmit={(e) => void send(e)} data-testid="chat-composer">
          <label className={local.composerLabel} htmlFor="chat-input">
            Message #{channelName}
          </label>
          <textarea
            id="chat-input"
            className={local.input}
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={onComposerKeyDown}
            placeholder={
              archived
                ? "This channel is archived"
                : canPost
                  ? "Write a message — Enter to send, Shift+Enter for a new line"
                  : "You have read-only access to this channel"
            }
            rows={2}
            disabled={!canPost || sending}
            aria-invalid={tooLong}
            aria-describedby="chat-counter"
            data-testid="chat-input"
          />
          <div className={local.composerFoot}>
            <span
              id="chat-counter"
              className={tooLong ? local.counterOver : local.counter}
              data-testid="chat-counter"
            >
              {length}/{MESSAGE_MAX_LENGTH}
              {tooLong ? " — too long" : ""}
            </span>
            <Button type="submit" variant="primary" disabled={!canSend} data-testid="chat-send">
              {sending ? "Sending…" : "Send"}
            </Button>
          </div>
          {sendError && (
            <p className={local.error} role="alert" data-testid="chat-send-error">
              {sendError}
            </p>
          )}
        </form>
      </section>
    </AppShell>
  );
}
