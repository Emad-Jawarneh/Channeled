import { describe, expect, it } from "vitest";
import { collapsePath } from "./collapsePath";

describe("collapsePath", () => {
  const segs = (names: string[]) => names.map((name, i) => ({ id: String(i), name }));

  it("returns short paths unchanged", () => {
    const input = segs(["root", "a", "b"]);
    expect(collapsePath(input)).toEqual(input);
  });

  it("collapses middle segments", () => {
    const input = segs(["root", "a", "b", "c", "d", "e"]);
    const out = collapsePath(input, 4, 2);
    expect(out.map((s) => s.name)).toEqual(["root", "…", "d", "e"]);
  });
});
