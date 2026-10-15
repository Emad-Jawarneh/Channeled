import { useQuery } from "@tanstack/react-query";
import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useReducer,
  useRef,
  useMemo,
  useState,
  type ChangeEvent,
  type FormEvent,
  type KeyboardEvent,
} from "react";
import {
  ChannelVisibility,
  MESSAGE_MAX_LENGTH,
  messagesApi,
  teamsApi,
  type ChannelMessage,
  type MessageMention,
  type PresenceUser,
  type TeamMember,
} from "../../api/channeled";
import { ApiError } from "../../api/http";
import { useAuth } from "../../auth/AuthContext";
import { AppShell } from "../../components/AppShell";
import { useAnnounce } from "../../components/liveAnnouncerContext";
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
import { ConfirmDialog } from "./ConfirmDialog";
import { findFileAttachQuery, type FileAttachQuery } from "./fileAttach";
import {
  FileAttachPicker,
  type FileAttachPick,
  type FileAttachPickerHandle,
} from "./FileAttachPicker";
import { activeMentionIds, filterMembers, findMentionQuery, type MentionQuery } from "./mentions";
import { addComposerFileIds, MAX_MESSAGE_FILE_REFS } from "./messageFileRefs";
import { formatDay } from "./chatFormat";
import { layoutMessages } from "./messageGrouping";
import { MessageRow } from "./MessageRow";
import local from "./TextChannelPage.module.css";

const REPLY_PREVIEW_LENGTH = 140;

function previewOf(body: string): string {
  const flat = body.replace(/\s+/g, " ").trim();
  const chars = Array.from(flat);
  return chars.length > REPLY_PREVIEW_LENGTH ? `${chars.slice(0, REPLY_PREVIEW_LENGTH).join("")}…` : flat;
}

function IconPlus({ className }: { className?: string }) {
  return (
    <svg className={className} width="22" height="22" viewBox="0 0 24 24" aria-hidden="true">
      <path fill="currentColor" d="M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6z" />
    </svg>
  );
}

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
  const [replyToId, setReplyToId] = useState<string | null>(null);
  const [trackedMentions, setTrackedMentions] = useState<MessageMention[]>([]);
  const [mentionQuery, setMentionQuery] = useState<MentionQuery | null>(null);
  const [mentionIndex, setMentionIndex] = useState(0);
  const [fileAttachQuery, setFileAttachQuery] = useState<FileAttachQuery | null>(null);
  const [attachPickerOpen, setAttachPickerOpen] = useState(false);
  const [composerFileIds, setComposerFileIds] = useState<string[]>([]);
  const [composerFileLabels, setComposerFileLabels] = useState<Record<string, string>>({});
  const [attachLimitError, setAttachLimitError] = useState<string | null>(null);
  const attachPickerRef = useRef<FileAttachPickerHandle>(null);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<ChannelMessage | null>(null);
  const [deleting, setDeleting] = useState(false);
  const announce = useAnnounce();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const focusOnArrival = useRef(true);
  const messagesRef = useRef<ChannelMessage[]>([]);
  messagesRef.current = state.messages;

  const members = useQuery({
    queryKey: ["members", teamSlug],
    queryFn: () => teamsApi.members(teamSlug),
  });

  const inputRef = useRef<HTMLTextAreaElement>(null);
  const pendingCaret = useRef<number | null>(null);
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
    setReplyToId(null);
    setTrackedMentions([]);
    setMentionQuery(null);
    setFileAttachQuery(null);
    setAttachPickerOpen(false);
    setComposerFileIds([]);
    setComposerFileLabels({});
    setAttachLimitError(null);
    setActiveId(null);
    setPendingDelete(null);
    stickToBottom.current = true;
    focusOnArrival.current = true;
    announce(`#${channelName}, text channel`);
  }, [teamSlug, channelName, announce]);

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

  const replyTarget = useMemo(
    () => (replyToId ? (state.messages.find((m) => m.id === replyToId) ?? null) : null),
    [replyToId, state.messages],
  );

  const mentionCandidates = useMemo(
    () =>
      mentionQuery
        ? filterMembers(
            (members.data ?? []).filter((m) => m.userId !== user?.id),
            mentionQuery.query,
          )
        : [],
    [mentionQuery, members.data, user?.id],
  );
  const mentionOpen = mentionQuery !== null && mentionCandidates.length > 0;

  const fileAttachOpen = attachPickerOpen || fileAttachQuery !== null;
  const attachFilter = fileAttachQuery?.query ?? "";

  const length = codePointLength(draft);
  const tooLong = length > MESSAGE_MAX_LENGTH;
  const canPost = canPostMessage(teamRole, archived);
  const canSend =
    canPost && !sending && !tooLong && (draft.trim().length > 0 || composerFileIds.length > 0);

  // Arriving in a channel: writers land in the composer, read-only viewers on the channel heading.
  useEffect(() => {
    if (!focusOnArrival.current || !channel || !team.data) return;
    focusOnArrival.current = false;
    if (canPost) inputRef.current?.focus();
    else headingRef.current?.focus();
  }, [channel, team.data, canPost]);

  async function send(event?: FormEvent) {
    event?.preventDefault();
    if (!canSend) return;
    setSending(true);
    setSendError(null);
    try {
      const message = await messagesApi.post(teamSlug, channelName, {
        body: draft,
        replyToMessageId: replyTarget?.id ?? null,
        mentionedUserIds: activeMentionIds(draft, trackedMentions),
        fileIds: composerFileIds,
      });
      stickToBottom.current = true;
      dispatch({ type: "created", message });
      setDraft("");
      setReplyToId(null);
      setTrackedMentions([]);
      setMentionQuery(null);
      setFileAttachQuery(null);
      setAttachPickerOpen(false);
      setComposerFileIds([]);
      setComposerFileLabels({});
      setAttachLimitError(null);
    } catch (error) {
      setSendError(describeError(error));
    } finally {
      setSending(false);
    }
  }

  function closeAttachPicker() {
    setAttachPickerOpen(false);
    setFileAttachQuery(null);
  }

  function updateComposerQueries(value: string, caret: number) {
    const fileNext = findFileAttachQuery(value, caret);
    if (fileNext) {
      setFileAttachQuery(fileNext);
      setAttachPickerOpen(true);
      setMentionQuery(null);
      return;
    }
    // Keep button-opened picker open until dismissed; only clear # token state.
    setFileAttachQuery(null);
    const mentionNext = findMentionQuery(value, caret);
    if (mentionQuery?.start !== mentionNext?.start || mentionQuery?.query !== mentionNext?.query) {
      setMentionIndex(0);
    }
    setMentionQuery(mentionNext);
  }

  function onDraftChange(event: ChangeEvent<HTMLTextAreaElement>) {
    setDraft(event.target.value);
    updateComposerQueries(event.target.value, event.target.selectionStart);
  }

  function addComposerFile(pick: FileAttachPick) {
    const { next, rejected } = addComposerFileIds(composerFileIds, [pick.id]);
    setComposerFileIds(next);
    setComposerFileLabels((current) => ({
      ...current,
      [pick.id]: `${pick.name} · ${pick.channelName}`,
    }));
    if (rejected.length > 0) {
      setAttachLimitError(`At most ${MAX_MESSAGE_FILE_REFS} files per message.`);
    } else {
      setAttachLimitError(null);
    }
    if (fileAttachQuery) {
      const caret = inputRef.current?.selectionStart ?? draft.length;
      const nextDraft = draft.slice(0, fileAttachQuery.start) + draft.slice(caret);
      pendingCaret.current = fileAttachQuery.start;
      setDraft(nextDraft);
    }
    closeAttachPicker();
  }

  function removeComposerFile(id: string) {
    setComposerFileIds((current) => current.filter((x) => x !== id));
    setComposerFileLabels((current) => {
      const next = { ...current };
      delete next[id];
      return next;
    });
    setAttachLimitError(null);
  }

  function startFileAttach() {
    if (!canPost) return;
    setMentionQuery(null);
    setFileAttachQuery(null);
    setAttachPickerOpen(true);
    inputRef.current?.focus();
  }

  function insertMention(member: TeamMember) {
    if (!mentionQuery) return;
    const caret = inputRef.current?.selectionStart ?? draft.length;
    const token = `@${member.displayName} `;
    const next = draft.slice(0, mentionQuery.start) + token + draft.slice(caret);
    pendingCaret.current = mentionQuery.start + token.length;
    setDraft(next);
    setTrackedMentions((current) =>
      current.some((m) => m.userId === member.userId)
        ? current
        : [...current, { userId: member.userId, displayName: member.displayName }],
    );
    setMentionQuery(null);
  }

  // Restore the caret after a programmatic draft change (mention insert / reply focus).
  useLayoutEffect(() => {
    if (pendingCaret.current !== null && inputRef.current) {
      const caret = pendingCaret.current;
      pendingCaret.current = null;
      inputRef.current.focus();
      inputRef.current.setSelectionRange(caret, caret);
    }
  }, [draft]);

  // Stable callbacks so memoized MessageRows skip re-rendering while the composer changes.
  const startReply = useCallback((message: ChannelMessage) => {
    setReplyToId(message.id);
    inputRef.current?.focus();
  }, []);

  const jumpToMessage = useCallback((id: string) => {
    const el = document.getElementById(`msg-${id}`);
    if (el) {
      stickToBottom.current = false;
      setActiveId(id);
      el.scrollIntoView({ block: "center", behavior: "smooth" });
      el.focus({ preventScroll: true });
    }
  }, []);

  const remove = useCallback(
    async (messageId: string): Promise<boolean> => {
      setActionError(null);
      try {
        await messagesApi.remove(teamSlug, channelName, messageId);
        dispatch({ type: "deleted", messageId });
        return true;
      } catch (error) {
        setActionError(describeError(error));
        return false;
      }
    },
    [teamSlug, channelName],
  );

  /** Deletes, then moves focus to the next message (or the previous one, or the composer). */
  const deleteAndRefocus = useCallback(
    async (message: ChannelMessage): Promise<boolean> => {
      const list = messagesRef.current;
      const index = list.findIndex((m) => m.id === message.id);
      const neighbor = list[index + 1] ?? list[index - 1] ?? null;
      const ok = await remove(message.id);
      if (ok) {
        window.setTimeout(() => {
          if (neighbor) {
            setActiveId(neighbor.id);
            document.getElementById(`msg-${neighbor.id}`)?.focus();
          } else {
            inputRef.current?.focus();
          }
        }, 0);
      }
      return ok;
    },
    [remove],
  );

  const requestDelete = useCallback(
    (message: ChannelMessage, skipConfirm: boolean) => {
      if (skipConfirm) void deleteAndRefocus(message);
      else setPendingDelete(message);
    },
    [deleteAndRefocus],
  );

  async function confirmDelete() {
    if (!pendingDelete) return;
    setDeleting(true);
    const ok = await deleteAndRefocus(pendingDelete);
    setDeleting(false);
    if (ok) setPendingDelete(null);
  }

  const layouts = useMemo(() => layoutMessages(state.messages, formatDay), [state.messages]);
  const activeMessageId =
    activeId && state.messages.some((m) => m.id === activeId) ? activeId : (state.messages.at(-1)?.id ?? null);

  /** Roving focus inside the log: arrows/Home/End move, R replies, Delete deletes (Shift skips confirm). */
  function onListKeyDown(event: KeyboardEvent<HTMLUListElement>) {
    const id = (event.target as HTMLElement).dataset.messageId;
    if (!id || event.ctrlKey || event.metaKey || event.altKey) return;
    const list = state.messages;
    const index = list.findIndex((m) => m.id === id);
    if (index < 0) return;
    const focusAt = (i: number) => {
      const target = list[i];
      if (!target) return;
      setActiveId(target.id);
      document.getElementById(`msg-${target.id}`)?.focus();
    };
    const message = list[index]!;
    switch (event.key) {
      case "ArrowUp":
        event.preventDefault();
        focusAt(Math.max(0, index - 1));
        break;
      case "ArrowDown":
        event.preventDefault();
        if (index === list.length - 1) inputRef.current?.focus();
        else focusAt(index + 1);
        break;
      case "Home":
        event.preventDefault();
        focusAt(0);
        break;
      case "End":
        event.preventDefault();
        focusAt(list.length - 1);
        break;
      case "r":
      case "R":
        if (canPost) {
          event.preventDefault();
          startReply(message);
        }
        break;
      case "Delete":
      case "Backspace":
        if (canDeleteMessage({ isAuthor: message.authorId === user?.id, teamRole })) {
          event.preventDefault();
          requestDelete(message, event.shiftKey);
        }
        break;
    }
  }

  function onComposerKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (fileAttachOpen && !event.nativeEvent.isComposing) {
      if (event.key === "ArrowDown") {
        event.preventDefault();
        attachPickerRef.current?.move(1);
        return;
      }
      if (event.key === "ArrowUp") {
        event.preventDefault();
        attachPickerRef.current?.move(-1);
        return;
      }
      if (event.key === "Enter" || event.key === "Tab") {
        event.preventDefault();
        attachPickerRef.current?.activate();
        return;
      }
      if (event.key === "Escape") {
        event.preventDefault();
        if (attachPickerRef.current?.back()) return;
        closeAttachPicker();
        return;
      }
    }
    if (mentionOpen && !event.nativeEvent.isComposing) {
      if (event.key === "ArrowDown") {
        event.preventDefault();
        setMentionIndex((i) => (i + 1) % mentionCandidates.length);
        return;
      }
      if (event.key === "ArrowUp") {
        event.preventDefault();
        setMentionIndex((i) => (i - 1 + mentionCandidates.length) % mentionCandidates.length);
        return;
      }
      if (event.key === "Enter" || event.key === "Tab") {
        event.preventDefault();
        const chosen = mentionCandidates[mentionIndex] ?? mentionCandidates[0];
        if (chosen) insertMention(chosen);
        return;
      }
      if (event.key === "Escape") {
        event.preventDefault();
        setMentionQuery(null);
        return;
      }
    }
    if (event.key === "Escape" && replyTarget) {
      setReplyToId(null);
      return;
    }
    if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void send();
    }
  }

  const sortedOnline = [...online].sort((a, b) => a.displayName.localeCompare(b.displayName));

  return (
    <AppShell
      title={`#${channelName}`}
      sidebar={<ChannelSidebar teamSlug={teamSlug} channelName={channelName} />}
      contentKey={`text:${teamSlug}/${channelName}`}
    >
      <section className={local.chat} aria-label={`Messages in ${channelName}`} data-testid="text-channel-home">
        <header className={local.chatHead}>
          <div>
            <h2 className={local.chatTitle} ref={headingRef} tabIndex={-1}>
              #{channelName}
            </h2>
            <p className={local.chatMeta}>
              {channel?.visibility === ChannelVisibility.Restricted ? "Restricted" : "Everyone"} text channel
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

          <ul className={local.messageList} onKeyDown={onListKeyDown}>
            {state.messages.map((message, index) => (
              <MessageRow
                key={message.id}
                message={message}
                teamSlug={teamSlug}
                myId={user?.id}
                grouped={layouts[index]?.grouped ?? false}
                dayLabel={layouts[index]?.dayLabel ?? null}
                canReply={canPost}
                canDelete={canDeleteMessage({ isAuthor: message.authorId === user?.id, teamRole })}
                active={message.id === activeMessageId}
                onReply={startReply}
                onDelete={requestDelete}
                onJumpTo={jumpToMessage}
                onActivate={setActiveId}
              />
            ))}
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
          {replyTarget && (
            <div className={local.replyChip} data-testid="chat-reply-chip">
              <span className={local.replyChipText}>
                Replying to <strong>{replyTarget.authorDisplayName}</strong>
                <span className={local.replyChipPreview}>{previewOf(replyTarget.body)}</span>
              </span>
              <button
                type="button"
                className={local.chipDismiss}
                onClick={() => setReplyToId(null)}
                aria-label="Cancel reply"
                data-testid="chat-reply-cancel"
              >
                ✕
              </button>
            </div>
          )}
          {composerFileIds.length > 0 && (
            <div className={local.composerFileChips} data-testid="composer-file-chips">
              {composerFileIds.map((id) => (
                <span key={id} className={local.composerFileChip}>
                  {composerFileLabels[id] ?? "File"}
                  <button
                    type="button"
                    className={local.chipDismiss}
                    aria-label="Remove attachment"
                    onClick={() => removeComposerFile(id)}
                  >
                    ✕
                  </button>
                </span>
              ))}
            </div>
          )}
          {attachLimitError && (
            <p className={local.error} role="alert">
              {attachLimitError}
            </p>
          )}
          <div className={local.composerBar}>
            <button
              type="button"
              className={local.plusButton}
              onClick={startFileAttach}
              disabled={!canPost || sending}
              aria-label={`Attach file (${composerFileIds.length}/${MAX_MESSAGE_FILE_REFS})`}
              title="Attach file"
              data-testid="chat-attach-file"
            >
              <IconPlus />
            </button>
            <div className={local.inputWrap}>
              <FileAttachPicker
                ref={attachPickerRef}
                open={fileAttachOpen}
                teamSlug={teamSlug}
                filter={attachFilter}
                excludeIds={composerFileIds}
                onPick={addComposerFile}
                onClose={closeAttachPicker}
              />
              {mentionOpen && (
                <ul
                  id="mention-listbox"
                  className={local.mentionList}
                  role="listbox"
                  aria-label="Mention a teammate"
                  data-testid="mention-list"
                >
                  {mentionCandidates.map((member, index) => (
                    <li
                      key={member.userId}
                      id={`mention-option-${member.userId}`}
                      role="option"
                      aria-selected={index === mentionIndex}
                      className={
                        index === mentionIndex
                          ? `${local.mentionOption} ${local.mentionOptionActive}`
                          : local.mentionOption
                      }
                      onMouseDown={(e) => {
                        // Keep focus (and caret) in the textarea.
                        e.preventDefault();
                        insertMention(member);
                      }}
                      onMouseEnter={() => setMentionIndex(index)}
                      data-testid="mention-option"
                    >
                      {member.displayName}
                    </li>
                  ))}
                </ul>
              )}
              <textarea
                id="chat-input"
                ref={inputRef}
                className={local.input}
                value={draft}
                onChange={onDraftChange}
                onSelect={(e) => updateComposerQueries(e.currentTarget.value, e.currentTarget.selectionStart)}
                onBlur={() => {
                  setMentionQuery(null);
                  // Keep hierarchical picker open so channel/file clicks register; dismiss via Close/Escape.
                }}
                onKeyDown={onComposerKeyDown}
                aria-controls={fileAttachOpen ? "file-attach-listbox" : mentionOpen ? "mention-listbox" : undefined}
                aria-autocomplete="list"
                aria-activedescendant={
                  mentionOpen && mentionCandidates[mentionIndex]
                    ? `mention-option-${mentionCandidates[mentionIndex]!.userId}`
                    : undefined
                }
                placeholder={
                  archived
                    ? "This channel is archived"
                    : canPost
                      ? `Message #${channelName}`
                      : "You have read-only access to this channel"
                }
                rows={1}
                disabled={!canPost || sending}
                aria-invalid={tooLong}
                aria-describedby="chat-counter"
                data-testid="chat-input"
              />
            </div>
            <Button type="submit" variant="primary" disabled={!canSend} data-testid="chat-send" className={local.sendButton}>
              {sending ? "…" : "Send"}
            </Button>
          </div>
          <div className={local.composerFoot}>
            <span
              id="chat-counter"
              className={tooLong ? local.counterOver : local.counter}
              data-testid="chat-counter"
            >
              {length}/{MESSAGE_MAX_LENGTH}
              {tooLong ? " — too long" : ""}
              {composerFileIds.length > 0
                ? ` · ${composerFileIds.length}/${MAX_MESSAGE_FILE_REFS} files`
                : ""}
            </span>
          </div>
          {sendError && (
            <p className={local.error} role="alert" data-testid="chat-send-error">
              {sendError}
            </p>
          )}
        </form>
      </section>
      <ConfirmDialog
        open={pendingDelete !== null}
        title="Delete message?"
        confirmLabel="Delete"
        busy={deleting}
        error={pendingDelete ? actionError : null}
        testId="chat-delete-dialog"
        onClose={() => {
          if (!deleting) setPendingDelete(null);
        }}
        onConfirm={confirmDelete}
      >
        <p>
          This removes the message for everyone in #{channelName}.
          {pendingDelete && (
            <>
              {" "}
              <strong>{pendingDelete.authorDisplayName}:</strong> {previewOf(pendingDelete.body) || "[file]"}
            </>
          )}
        </p>
        <p>Tip: hold Shift while clicking delete to skip this confirmation.</p>
      </ConfirmDialog>
    </AppShell>
  );
}
