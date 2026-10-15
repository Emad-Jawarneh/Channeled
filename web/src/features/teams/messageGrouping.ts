import type { ChannelMessage } from "../../api/channeled";

/** Consecutive messages from one author within this window share a header (Discord-style). */
export const GROUP_WINDOW_MS = 7 * 60 * 1000;

export type MessageLayout = {
  /** True when the row continues the previous author's group (no avatar/name header). */
  grouped: boolean;
  /** Day divider label rendered above this row, or null. */
  dayLabel: string | null;
};

function localDayKey(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
}

/** Layout for messages ordered oldest → newest; the result aligns with `messages` by index. */
export function layoutMessages(
  messages: readonly ChannelMessage[],
  formatDay: (iso: string) => string,
): MessageLayout[] {
  const out: MessageLayout[] = [];
  let previous: ChannelMessage | null = null;
  let lastDay = "";
  for (const message of messages) {
    const day = localDayKey(message.createdAt);
    const newDay = day !== lastDay;
    lastDay = day;
    let grouped = false;
    if (previous && !newDay && !message.replyTo && previous.authorId === message.authorId) {
      const gap = Date.parse(message.createdAt) - Date.parse(previous.createdAt);
      grouped = gap >= 0 && gap <= GROUP_WINDOW_MS;
    }
    out.push({ grouped, dayLabel: newDay ? formatDay(message.createdAt) : null });
    previous = message;
  }
  return out;
}
