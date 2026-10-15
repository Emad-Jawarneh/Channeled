import { describe, expect, it } from "vitest";
import type { FileItem, ItemList } from "../../api/channeled";
import { mergePages, selectionLabel, sortParam } from "./pagedItems";

const item = (id: string): FileItem => ({
  id,
  kind: 0,
  name: id,
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
});
const page = (...ids: string[]): ItemList => ({ items: ids.map(item), nextCursor: null });

describe("mergePages", () => {
  it("keeps page order and drops repeats from overlapping refetches", () => {
    expect(mergePages([page("a", "b"), page("b", "c"), page("d")]).map((i) => i.id)).toEqual(["a", "b", "c", "d"]);
  });

  it("is empty before the first page arrives", () => {
    expect(mergePages(undefined)).toEqual([]);
  });
});

describe("sortParam", () => {
  it("maps a column and direction to the server token", () => {
    expect(sortParam("name", "asc")).toBe("name_asc");
    expect(sortParam("updated", "desc")).toBe("updated_desc");
  });
});

describe("selectionLabel", () => {
  it("says the selection only covers loaded items while more pages exist", () => {
    expect(selectionLabel(12, true)).toBe("12 selected · loaded items only");
    expect(selectionLabel(12, false)).toBe("12 selected");
  });
});
