import { describe, expect, it } from "vitest";
import { ItemKind, type FileItem } from "../../api/channeled";
import { compareFileItems } from "./sortItems";

function item(name: string, kind: number): FileItem {
  return {
    id: name,
    name,
    kind,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  };
}

describe("compareFileItems name sort", () => {
  it("lists folders before files, then by name", () => {
    const rows = [
      item("zeta.txt", ItemKind.File),
      item("Beta", ItemKind.Folder),
      item("alpha.txt", ItemKind.File),
      item("Alpha", ItemKind.Folder),
    ];
    const sorted = [...rows].sort((a, b) => compareFileItems(a, b, "name", "asc"));
    expect(sorted.map((r) => r.name)).toEqual(["Alpha", "Beta", "alpha.txt", "zeta.txt"]);
  });

  it("keeps folders first when name sort is descending", () => {
    const rows = [
      item("a.txt", ItemKind.File),
      item("Z", ItemKind.Folder),
      item("z.txt", ItemKind.File),
      item("A", ItemKind.Folder),
    ];
    const sorted = [...rows].sort((a, b) => compareFileItems(a, b, "name", "desc"));
    expect(sorted.map((r) => r.name)).toEqual(["Z", "A", "z.txt", "a.txt"]);
  });
});
