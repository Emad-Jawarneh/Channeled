import { describe, expect, it } from "vitest";
import {
  addComposerFileIds,
  hrefForResolvedFile,
  labelForResolvedFile,
  normalizeComposerFileIds,
} from "./messageFileRefs";

describe("normalizeComposerFileIds", () => {
  it("de-dupes and preserves order", () => {
    expect(normalizeComposerFileIds(["a", "b", "a", "c", "b"])).toEqual(["a", "b", "c"]);
  });
});

describe("addComposerFileIds", () => {
  it("caps at five attachments", () => {
    const ids = ["1", "2", "3", "4", "5", "6"];
    const { next, rejected } = addComposerFileIds([], ids);
    expect(next).toHaveLength(5);
    expect(rejected).toEqual(["6"]);
  });
});

describe("labelForResolvedFile", () => {
  it("labels inaccessible without leaking metadata", () => {
    expect(
      labelForResolvedFile({
        fileId: "x",
        accessible: false,
        name: "secret",
        state: "missing",
      }),
    ).toBe("A file you don't have access to");
  });

  it("labels trashed and missing when accessible", () => {
    expect(
      labelForResolvedFile({
        fileId: "x",
        accessible: true,
        name: "doc.pdf",
        state: "trashed",
      }),
    ).toBe("doc.pdf (in trash)");
    expect(
      labelForResolvedFile({
        fileId: "x",
        accessible: true,
        state: "missing",
      }),
    ).toBe("File no longer available");
  });
});

describe("hrefForResolvedFile", () => {
  it("links live files into their channel folder with item highlight", () => {
    expect(
      hrefForResolvedFile("acme", {
        fileId: "f1",
        accessible: true,
        channelName: "docs",
        folderId: "11111111-1111-1111-1111-111111111111",
        state: "live",
      }),
    ).toBe(
      "/t/acme/c/docs?folder=11111111-1111-1111-1111-111111111111&item=f1",
    );
  });

  it("links trashed files to channel trash", () => {
    expect(
      hrefForResolvedFile("acme", {
        fileId: "f1",
        accessible: true,
        channelName: "docs",
        state: "trashed",
      }),
    ).toBe("/t/acme/c/docs/trash?item=f1");
  });

  it("returns null when inaccessible or missing", () => {
    expect(
      hrefForResolvedFile("acme", {
        fileId: "f1",
        accessible: false,
        channelName: "docs",
        state: "missing",
      }),
    ).toBeNull();
  });
});
