import { afterEach, describe, expect, it } from "vitest";
import {
  clearFileClipboard,
  clipboardLabel,
  getFileClipboard,
  pruneFileClipboard,
  setFileClipboard,
} from "./fileClipboard";

afterEach(() => {
  clearFileClipboard();
});

describe("fileClipboard store", () => {
  it("sets cut/copy state and clears", () => {
    setFileClipboard({
      mode: "cut",
      items: [
        { id: "a", name: "a.txt", kind: 0 },
        { id: "a", name: "a.txt", kind: 0 },
        { id: "b", name: "b.txt", kind: 0 },
      ],
      source: { scope: "personal", folderId: null },
    });
    expect(getFileClipboard()?.items.map((i) => i.id)).toEqual(["a", "b"]);
    expect(clipboardLabel(getFileClipboard()!)).toBe("2 items cut");
    clearFileClipboard();
    expect(getFileClipboard()).toBeNull();
  });

  it("prunes vanished ids and clears when empty", () => {
    setFileClipboard({
      mode: "copy",
      items: [
        { id: "a", name: "a.txt", kind: 0 },
        { id: "b", name: "b.txt", kind: 0 },
      ],
      source: { scope: "channel", teamSlug: "t", channelName: "c", folderId: null },
    });
    pruneFileClipboard(["a"]);
    expect(getFileClipboard()?.items.map((i) => i.id)).toEqual(["a"]);
    pruneFileClipboard([]);
    expect(getFileClipboard()).toBeNull();
  });
});
