import { describe, expect, it } from "vitest";
import type { ChannelMessage } from "../../api/channeled";
import { GROUP_WINDOW_MS, layoutMessages } from "./messageGrouping";

const base = new Date(2026, 9, 15, 10, 0, 0).getTime();

function msg(id: string, authorId: string, offsetMs: number, reply = false): ChannelMessage {
  return {
    id,
    channelId: "c1",
    authorId,
    authorDisplayName: authorId,
    body: id,
    createdAt: new Date(base + offsetMs).toISOString(),
    replyTo: reply ? { id: "p", authorId: "x", authorDisplayName: "X", bodyPreview: "p", deleted: false } : null,
    mentions: [],
    fileIds: [],
  };
}

const day = (iso: string) => new Date(iso).toDateString();

describe("layoutMessages", () => {
  it("groups consecutive messages from one author inside the window", () => {
    const layout = layoutMessages([msg("a", "ada", 0), msg("b", "ada", 60_000), msg("c", "ada", 120_000)], day);
    expect(layout.map((l) => l.grouped)).toEqual([false, true, true]);
  });

  it("breaks the group on author change", () => {
    const layout = layoutMessages([msg("a", "ada", 0), msg("b", "bob", 1_000), msg("c", "ada", 2_000)], day);
    expect(layout.map((l) => l.grouped)).toEqual([false, false, false]);
  });

  it("breaks the group when the gap exceeds the window", () => {
    const layout = layoutMessages([msg("a", "ada", 0), msg("b", "ada", GROUP_WINDOW_MS), msg("c", "ada", 2 * GROUP_WINDOW_MS + 1)], day);
    expect(layout.map((l) => l.grouped)).toEqual([false, true, false]);
  });

  it("never groups a reply", () => {
    const layout = layoutMessages([msg("a", "ada", 0), msg("b", "ada", 1_000, true)], day);
    expect(layout[1]!.grouped).toBe(false);
  });

  it("starts a new group and a day divider at a day boundary", () => {
    const lateNight = new Date(2026, 9, 15, 23, 59, 0).getTime() - base;
    const nextMorning = new Date(2026, 9, 16, 0, 1, 0).getTime() - base;
    const layout = layoutMessages([msg("a", "ada", lateNight), msg("b", "ada", nextMorning)], day);
    expect(layout.map((l) => l.grouped)).toEqual([false, false]);
    expect(layout.every((l) => l.dayLabel !== null)).toBe(true);
  });

  it("emits one day divider per day", () => {
    const layout = layoutMessages([msg("a", "ada", 0), msg("b", "bob", 1_000), msg("c", "ada", 2_000)], day);
    expect(layout.map((l) => l.dayLabel !== null)).toEqual([true, false, false]);
  });
});
