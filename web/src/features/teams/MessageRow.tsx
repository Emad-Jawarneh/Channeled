import { memo, useState, type MouseEvent } from "react";
import type { ChannelMessage } from "../../api/channeled";
import { formatTime } from "./chatFormat";
import { segmentBody } from "./mentions";
import { MessageFileChips } from "./MessageFileChips";
import local from "./TextChannelPage.module.css";

function authorInitial(name: string): string {
  const ch = Array.from(name.trim())[0];
  return (ch ?? "?").toUpperCase();
}

/** Stable hue from an id for the initials fallback. */
function avatarHue(id: string): number {
  let h = 0;
  for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) >>> 0;
  return h % 360;
}

function IconReply() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
      <path fill="currentColor" d="M10 9V5l-7 7 7 7v-4.1c5 0 8.5 1.6 11 5.1-1-5-4-10-11-11z" />
    </svg>
  );
}

function IconTrash() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
      <path
        fill="currentColor"
        d="M6 19a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V7H6zm3.5-9h1v9h-1zm3 0h1v9h-1zM15.5 4l-1-1h-5l-1 1H5v2h14V4z"
      />
    </svg>
  );
}

function ChatAvatar({ userId, name, avatarVersion }: { userId: string; name: string; avatarVersion?: number | null }) {
  const [failed, setFailed] = useState(false);
  if (avatarVersion && !failed) {
    return (
      <img
        className={local.avatar}
        src={`/api/v1/users/${userId}/avatar?v=${avatarVersion}`}
        alt=""
        loading="lazy"
        data-testid="chat-avatar"
        data-avatar="image"
        onError={() => setFailed(true)}
      />
    );
  }
  return (
    <div
      className={local.avatar}
      style={{ backgroundColor: `hsl(${avatarHue(userId)} 42% 38%)` }}
      aria-hidden="true"
      data-testid="chat-avatar"
      data-avatar="initials"
    >
      {authorInitial(name)}
    </div>
  );
}

function MessageBody({ message, myId }: { message: ChannelMessage; myId: string | undefined }) {
  const segments = segmentBody(message.body, message.mentions ?? []);
  return (
    <p className={local.body} id={`msg-${message.id}-body`}>
      {segments.map((segment, index) =>
        segment.mention ? (
          <span
            key={index}
            className={segment.mention.userId === myId ? `${local.mention} ${local.mentionSelf}` : local.mention}
            data-testid="chat-mention"
          >
            {segment.text}
          </span>
        ) : (
          segment.text
        ),
      )}
    </p>
  );
}

export type MessageRowProps = {
  message: ChannelMessage;
  teamSlug: string;
  myId: string | undefined;
  grouped: boolean;
  dayLabel: string | null;
  canReply: boolean;
  canDelete: boolean;
  /** Holds the roving tab stop for the message list. */
  active: boolean;
  onReply: (message: ChannelMessage) => void;
  onDelete: (message: ChannelMessage, skipConfirm: boolean) => void;
  onJumpTo: (messageId: string) => void;
  onActivate: (messageId: string) => void;
};

/**
 * One chat message. Memoized: typing in the composer or toggling another row must not re-render the
 * whole log (perf-probe measured ~760 ms interactions at 1,000 messages before this split).
 */
export const MessageRow = memo(function MessageRow({
  message,
  teamSlug,
  myId,
  grouped,
  dayLabel,
  canReply,
  canDelete,
  active,
  onReply,
  onDelete,
  onJumpTo,
  onActivate,
}: MessageRowProps) {
  const mentionsMe = message.mentions?.some((m) => m.userId === myId);
  const time = formatTime(message.createdAt);
  const fullTime = new Date(message.createdAt).toLocaleString();
  const tab = active ? 0 : -1;
  const className = [local.message, grouped ? local.messageGrouped : "", mentionsMe ? local.messageMentionsMe : ""]
    .filter(Boolean)
    .join(" ");

  return (
    <li className={local.messageItem} data-testid="chat-message" data-grouped={grouped ? "true" : "false"}>
      {dayLabel && (
        <div className={local.day} role="separator" aria-label={dayLabel}>
          <span className={local.dayLine} aria-hidden="true" />
          <span className={local.dayLabel}>{dayLabel}</span>
          <span className={local.dayLine} aria-hidden="true" />
        </div>
      )}
      <div
        id={`msg-${message.id}`}
        className={className}
        role="article"
        tabIndex={tab}
        data-message-id={message.id}
        aria-labelledby={`msg-${message.id}-author msg-${message.id}-time`}
        aria-describedby={`msg-${message.id}-body`}
        onFocus={(e) => {
          if (e.target === e.currentTarget) onActivate(message.id);
        }}
      >
        {(canReply || canDelete) && (
          <div className={local.hoverBar} role="toolbar" aria-label="Message actions">
            {canReply && (
              <button
                type="button"
                className={local.iconButton}
                tabIndex={tab}
                onClick={() => onReply(message)}
                aria-label={`Reply to ${message.authorDisplayName} at ${time}`}
                data-testid="chat-reply"
                title="Reply (R)"
              >
                <IconReply />
              </button>
            )}
            {canDelete && (
              <button
                type="button"
                className={`${local.iconButton} ${local.iconButtonDanger}`}
                tabIndex={tab}
                onClick={(e: MouseEvent) => onDelete(message, e.shiftKey)}
                aria-label={`Delete message from ${message.authorDisplayName} at ${time}`}
                data-testid="chat-delete"
                title="Delete (Shift+click skips confirmation)"
              >
                <IconTrash />
              </button>
            )}
          </div>
        )}
        <div className={local.gutter}>
          {grouped ? (
            <time className={local.gutterTime} dateTime={message.createdAt} title={fullTime}>
              {time}
            </time>
          ) : (
            <ChatAvatar userId={message.authorId} name={message.authorDisplayName} avatarVersion={message.authorAvatarVersion} />
          )}
        </div>
        <div className={local.messageBody}>
          {message.replyTo &&
            (message.replyTo.deleted ? (
              <p className={`${local.quote} ${local.quoteDeleted}`} data-testid="chat-reply-quote">
                Replying to a deleted message
              </p>
            ) : (
              <button
                type="button"
                className={local.quote}
                tabIndex={tab}
                onClick={() => onJumpTo(message.replyTo!.id)}
                aria-label={`Replying to ${message.replyTo.authorDisplayName}: ${message.replyTo.bodyPreview}`}
                data-testid="chat-reply-quote"
              >
                <span className={local.quoteAuthor}>{message.replyTo.authorDisplayName}</span>
                <span className={local.quoteText}>{message.replyTo.bodyPreview}</span>
              </button>
            ))}
          {grouped ? (
            <span className="visually-hidden">
              <span id={`msg-${message.id}-author`}>{message.authorDisplayName}</span>{" "}
              <span id={`msg-${message.id}-time`}>{time}</span>
            </span>
          ) : (
            <div className={local.messageHead}>
              <span className={local.author} id={`msg-${message.id}-author`}>
                {message.authorDisplayName}
              </span>
              <time className={local.time} dateTime={message.createdAt} title={fullTime} id={`msg-${message.id}-time`}>
                {time}
              </time>
            </div>
          )}
          {/* Plain text only: React escapes it and pre-wrap keeps line breaks. */}
          <MessageBody message={message} myId={myId} />
          <MessageFileChips teamSlug={teamSlug} fileIds={message.fileIds ?? []} />
        </div>
      </div>
    </li>
  );
});
