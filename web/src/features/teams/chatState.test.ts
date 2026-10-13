import { describe, expect, it } from "vitest";
import type { ChannelMessage, MessageList } from "../../api/channeled";
import {
  canDeleteMessage,
  canPostMessage,
  chatReducer,
  compareMessages,
  initialChatState,
  timestampMicros,
  type ChatState,
} from "./chatState";

function msg(id: string, createdAt: string, body = id): ChannelMessage {
  return { id, channelId: "c1", authorId: "u1", authorDisplayName: "Ada", body, createdAt };
}

// Server pages newest-first.
function page(items: ChannelMessage[], nextCursor: string | null = null): MessageList {
  return { items, nextCursor };
}

const noBuffer = () => ({ created: [], deleted: new Set<string>() });

describe("chatState ordering", () => {
  it("orders by microsecond timestamp then id, even when .NET trims trailing zeros", () => {
    const a = msg("a", "2026-01-01T00:00:00.1234+00:00");
    const b = msg("b", "2026-01-01T00:00:00.123456+00:00");
    expect(compareMessages(a, b)).toBeLessThan(0);
    expect(timestampMicros("2026-01-01T00:00:00+00:00") % 1000).toBe(0);
  });

  it("breaks identical created_at ties by id", () => {
    const t = "2026-01-01T00:00:00+00:00";
    expect(compareMessages(msg("a", t), msg("b", t))).toBeLessThan(0);
    expect(compareMessages(msg("b", t), msg("a", t))).toBeGreaterThan(0);
  });
});

describe("chatReducer", () => {
  const t1 = "2026-01-01T00:00:01+00:00";
  const t2 = "2026-01-01T00:00:02+00:00";
  const t3 = "2026-01-01T00:00:03+00:00";

  it("dedupes created events (REST echo + hub push) and keeps order", () => {
    let s: ChatState = initialChatState;
    s = chatReducer(s, { type: "created", message: msg("b", t2) });
    s = chatReducer(s, { type: "created", message: msg("a", t1) });
    s = chatReducer(s, { type: "created", message: msg("b", t2) });
    expect(s.messages.map((m) => m.id)).toEqual(["a", "b"]);
  });

  it("removes deleted messages", () => {
    let s = chatReducer(initialChatState, { type: "created", message: msg("a", t1) });
    s = chatReducer(s, { type: "deleted", messageId: "a" });
    expect(s.messages).toEqual([]);
  });

  it("resync with a complete page replaces local state (reconciles missed deletes)", () => {
    let s = chatReducer(initialChatState, { type: "created", message: msg("gone", t1) });
    s = chatReducer(s, { type: "created", message: msg("kept", t2) });
    s = chatReducer(s, { type: "resync", page: page([msg("kept", t2), msg("new", t3)]), buffer: noBuffer() });
    expect(s.messages.map((m) => m.id)).toEqual(["kept", "new"]);
    expect(s.olderCursor).toBeNull();
    expect(s.loaded).toBe(true);
  });

  it("resync with a partial page keeps already-loaded older history and drops stale rows in the window", () => {
    const old = msg("old", "2025-12-31T00:00:00+00:00");
    const t15 = "2026-01-01T00:00:01.5+00:00";
    let s: ChatState = {
      messages: [old, msg("first", t1), msg("stale", t15), msg("kept", t2)],
      olderCursor: "older-cursor",
      loaded: true,
    };
    // Fetched window spans t1..t3, so "stale" (t1.5, deleted while offline) is inside it and gone.
    s = chatReducer(s, {
      type: "resync",
      page: page([msg("new", t3), msg("kept", t2), msg("first", t1)], "next"),
      buffer: noBuffer(),
    });
    expect(s.messages.map((m) => m.id)).toEqual(["old", "first", "kept", "new"]);
    expect(s.olderCursor).toBe("older-cursor");
  });

  it("resync with a partial page and no prior history adopts the page cursor", () => {
    const s = chatReducer(initialChatState, {
      type: "resync",
      page: page([msg("b", t2), msg("a", t1)], "cursor-1"),
      buffer: noBuffer(),
    });
    expect(s.messages.map((m) => m.id)).toEqual(["a", "b"]);
    expect(s.olderCursor).toBe("cursor-1");
  });

  it("replays events that arrived while the resync fetch was in flight", () => {
    const buffer = { created: [msg("late", t3)], deleted: new Set(["b"]) };
    const s = chatReducer(initialChatState, {
      type: "resync",
      page: page([msg("b", t2), msg("a", t1)]),
      buffer,
    });
    expect(s.messages.map((m) => m.id)).toEqual(["a", "late"]);
  });

  it("prepends older pages and tracks exhaustion", () => {
    let s = chatReducer(initialChatState, {
      type: "resync",
      page: page([msg("c", t3)], "cursor-1"),
      buffer: noBuffer(),
    });
    s = chatReducer(s, { type: "older", page: page([msg("b", t2), msg("a", t1)]) });
    expect(s.messages.map((m) => m.id)).toEqual(["a", "b", "c"]);
    expect(s.olderCursor).toBeNull();
  });
});

describe("UI permission hints", () => {
  it("lets authors (Member+) and moderators (Admin+) delete; never viewers", () => {
    expect(canDeleteMessage({ isAuthor: true, teamRole: 1 })).toBe(true);
    expect(canDeleteMessage({ isAuthor: false, teamRole: 1 })).toBe(false);
    expect(canDeleteMessage({ isAuthor: false, teamRole: 2 })).toBe(true);
    expect(canDeleteMessage({ isAuthor: true, teamRole: 0 })).toBe(false);
    expect(canDeleteMessage({ isAuthor: true, teamRole: undefined })).toBe(false);
  });

  it("blocks posting for viewers and archived channels", () => {
    expect(canPostMessage(1, false)).toBe(true);
    expect(canPostMessage(0, false)).toBe(false);
    expect(canPostMessage(3, true)).toBe(false);
  });
});
