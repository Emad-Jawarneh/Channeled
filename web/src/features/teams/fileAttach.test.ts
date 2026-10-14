import { describe, expect, it } from "vitest";
import { findFileAttachQuery } from "./fileAttach";

describe("findFileAttachQuery", () => {
  it("detects # at end of draft", () => {
    expect(findFileAttachQuery("hello #", 7)).toEqual({ start: 6, query: "" });
  });

  it("captures filter text after #", () => {
    expect(findFileAttachQuery("#docs", 5)).toEqual({ start: 0, query: "docs" });
  });

  it("ignores # mid-word", () => {
    expect(findFileAttachQuery("c#code", 6)).toBeNull();
  });
});
