import { describe, expect, it } from "vitest";
import type { ItemLocation } from "../../api/channeled";
import { focusIdFor, locationMatchesView, pathForLocation } from "./locate";

const file = { id: "f1", kind: 0, name: "f.txt", createdAt: "", updatedAt: "" };

const live: ItemLocation = {
  state: "live",
  scope: "channel",
  teamSlug: "acme",
  channelName: "Docs",
  folderId: "dir",
  folderPath: [{ id: "dir", name: "Dir" }],
  item: file,
};

describe("pathForLocation", () => {
  it("opens live channel items in their folder with the row highlighted", () => {
    expect(pathForLocation(live)).toBe("/t/acme/c/Docs?folder=dir&item=f1");
  });

  it("opens root items without a folder param", () => {
    expect(pathForLocation({ ...live, folderId: null, folderPath: [] })).toBe("/t/acme/c/Docs?item=f1");
  });

  it("opens trashed items in the trash view on their trash entry", () => {
    const trashed: ItemLocation = { ...live, state: "trashed", trashEntryId: "outer" };
    expect(focusIdFor(trashed)).toBe("outer");
    expect(pathForLocation(trashed)).toBe("/t/acme/c/Docs/trash?item=outer");
  });

  it("opens personal items under /me/files", () => {
    expect(pathForLocation({ ...live, scope: "personal", teamSlug: null, channelName: null })).toBe(
      "/me/files?folder=dir&item=f1",
    );
  });
});

describe("locationMatchesView", () => {
  const view = { scope: "channel" as const, teamSlug: "acme", channelName: "docs", folderId: "dir", trash: false };

  it("matches the same channel (case-insensitive) and folder", () => {
    expect(locationMatchesView(live, view)).toBe(true);
  });

  it("does not match another folder, channel, team or the trash view", () => {
    expect(locationMatchesView(live, { ...view, folderId: null })).toBe(false);
    expect(locationMatchesView(live, { ...view, channelName: "other" })).toBe(false);
    expect(locationMatchesView(live, { ...view, teamSlug: "other" })).toBe(false);
    expect(locationMatchesView(live, { ...view, trash: true })).toBe(false);
  });

  it("matches trashed items only in the trash view", () => {
    const trashed: ItemLocation = { ...live, state: "trashed", trashEntryId: "f1" };
    expect(locationMatchesView(trashed, { ...view, trash: true })).toBe(true);
    expect(locationMatchesView(trashed, view)).toBe(false);
  });
});
