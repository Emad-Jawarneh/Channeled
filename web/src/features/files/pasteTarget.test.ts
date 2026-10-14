import { describe, expect, it } from "vitest";
import { pasteTargetFromLocation } from "./pasteTarget";

describe("pasteTargetFromLocation", () => {
  it("resolves personal files root and folder", () => {
    expect(pasteTargetFromLocation("/me/files", "")).toEqual({
      scope: "personal",
      folderId: null,
    });
    expect(
      pasteTargetFromLocation("/me/files", "?folder=11111111-1111-1111-1111-111111111111"),
    ).toEqual({
      scope: "personal",
      folderId: "11111111-1111-1111-1111-111111111111",
    });
  });

  it("rejects personal trash/recent/starred", () => {
    expect(pasteTargetFromLocation("/me/files/trash", "")).toBeNull();
    expect(pasteTargetFromLocation("/me/files/recent", "")).toBeNull();
  });

  it("resolves team channel paths", () => {
    expect(pasteTargetFromLocation("/t/acme/c/docs", "")).toEqual({
      scope: "channel",
      teamSlug: "acme",
      channelName: "docs",
      folderId: null,
    });
    expect(pasteTargetFromLocation("/t/acme/c/docs/trash", "")).toBeNull();
  });
});
