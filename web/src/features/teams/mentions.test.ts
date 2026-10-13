import { describe, expect, it } from "vitest";
import { activeMentionIds, filterMembers, findMentionQuery, segmentBody } from "./mentions";

describe("findMentionQuery", () => {
  it("finds an @token before the caret", () => {
    expect(findMentionQuery("hi @ad", 6)).toEqual({ start: 3, query: "ad" });
    expect(findMentionQuery("@", 1)).toEqual({ start: 0, query: "" });
  });

  it("ignores emails and tokens followed by whitespace", () => {
    expect(findMentionQuery("a@b", 3)).toBeNull();
    expect(findMentionQuery("@ada hi", 7)).toBeNull();
  });
});

describe("filterMembers", () => {
  const members = [{ displayName: "Grace" }, { displayName: "Ada" }, { displayName: "Nadia" }];
  it("puts prefix matches before substring matches", () => {
    expect(filterMembers(members, "ad").map((m) => m.displayName)).toEqual(["Ada", "Nadia"]);
  });
});

describe("segmentBody", () => {
  const mentions = [
    { userId: "1", displayName: "Ada Lovelace" },
    { userId: "2", displayName: "Ada" },
  ];
  it("highlights the longest matching name", () => {
    const parts = segmentBody("hey @Ada Lovelace and @Ada!", mentions);
    expect(parts.map((p) => p.mention?.userId)).toEqual([undefined, "1", undefined, "2", undefined]);
  });

  it("does not highlight when glued to word characters", () => {
    expect(segmentBody("@Adam", mentions)).toEqual([{ text: "@Adam" }]);
  });
});

describe("activeMentionIds", () => {
  it("drops mentions that were edited out", () => {
    const tracked = [
      { userId: "1", displayName: "Ada" },
      { userId: "2", displayName: "Bob" },
    ];
    expect(activeMentionIds("hi @Ada", tracked)).toEqual(["1"]);
  });
});
