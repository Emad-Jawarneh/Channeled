import type { ChannelMessage, MessageList } from "../../api/channeled";

/** Messages are kept oldest → newest, ordered by the same (created_at, id) key the server pages by. */
export type ChatState = {
  messages: ChannelMessage[];
  /** Cursor for the next OLDER page; null once history is exhausted; undefined before the first load. */
  olderCursor: string | null | undefined;
  loaded: boolean;
};

export const initialChatState: ChatState = { messages: [], olderCursor: undefined, loaded: false };

/** Events that arrived while a resync fetch was in flight (the fetched page may predate them). */
export type ResyncBuffer = { created: ChannelMessage[]; deleted: Set<string> };

export type ChatAction =
  | { type: "reset" }
  | { type: "created"; message: ChannelMessage }
  | { type: "deleted"; messageId: string }
  | { type: "resync"; page: MessageList; buffer: ResyncBuffer }
  | { type: "older"; page: MessageList };

/** Microsecond-precision timestamp (Date.parse alone would drop sub-millisecond ordering). */
export function timestampMicros(iso: string): number {
  const ms = Date.parse(iso);
  const fraction = /\.(\d+)/.exec(iso)?.[1] ?? "";
  const subMs = Number(fraction.padEnd(6, "0").slice(3, 6));
  return ms * 1000 + subMs;
}

export function compareMessages(a: ChannelMessage, b: ChannelMessage): number {
  const diff = timestampMicros(a.createdAt) - timestampMicros(b.createdAt);
  if (diff !== 0) {
    return diff;
  }
  return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
}

function merge(...lists: ChannelMessage[][]): ChannelMessage[] {
  const byId = new Map<string, ChannelMessage>();
  for (const list of lists) {
    for (const message of list) {
      byId.set(message.id, message);
    }
  }
  return [...byId.values()].sort(compareMessages);
}

export function chatReducer(state: ChatState, action: ChatAction): ChatState {
  switch (action.type) {
    case "reset":
      return initialChatState;

    case "created":
      return { ...state, messages: merge(state.messages, [action.message]) };

    case "deleted":
      return { ...state, messages: state.messages.filter((m) => m.id !== action.messageId) };

    case "older": {
      const items = [...action.page.items].reverse();
      return {
        ...state,
        messages: merge(items, state.messages),
        olderCursor: action.page.nextCursor ?? null,
      };
    }

    case "resync": {
      const fetched = [...action.page.items].reverse();
      const visible = (m: ChannelMessage) => !action.buffer.deleted.has(m.id);
      const latest = merge(fetched, action.buffer.created).filter(visible);

      if (action.page.nextCursor == null) {
        // The page is the whole channel: anything we hold that it lacks was deleted (or never existed).
        return { messages: latest, olderCursor: null, loaded: true };
      }

      // The page is only the newest window. Drop stale rows inside it, keep older history already loaded.
      const oldestFetched = fetched[0];
      const older = oldestFetched
        ? state.messages.filter((m) => compareMessages(m, oldestFetched) < 0 && visible(m))
        : [];
      return {
        messages: merge(older, latest),
        olderCursor: older.length > 0 ? state.olderCursor : action.page.nextCursor,
        loaded: true,
      };
    }
  }
}

export function canDeleteMessage(opts: { isAuthor: boolean; teamRole: number | null | undefined }): boolean {
  // UI hint only: the server authorizes every delete. Author needs Member+, moderation needs Admin+.
  const role = opts.teamRole ?? -1;
  return role >= 2 || (opts.isAuthor && role >= 1);
}

export function canPostMessage(teamRole: number | null | undefined, archived: boolean): boolean {
  return !archived && (teamRole ?? -1) >= 1;
}
