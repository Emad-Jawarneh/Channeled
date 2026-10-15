import { describe, expect, it } from "vitest";
import type { SearchHit, Team } from "../../api/channeled";
import { hitLocationLabel, teamLabelForHit } from "./hitLocation";

const hit = (partial: Partial<SearchHit>): SearchHit => ({
  id: "1",
  kind: 0,
  name: "file.txt",
  updatedAt: new Date().toISOString(),
  ...partial,
});

const team = (partial: Partial<Team>): Team => ({
  id: "t1",
  name: "Acme Corp",
  slug: "acme",
  quotaBytes: 0,
  joinMode: 0,
  defaultJoinRole: 1,
  ...partial,
});

describe("teamLabelForHit", () => {
  it("prefers teamName from the hit", () => {
    expect(
      teamLabelForHit(hit({ teamSlug: "acme", teamName: "Renamed" }), [team({ name: "Stale" })]),
    ).toBe("Renamed");
  });

  it("falls back to the live teams list when teamName is missing", () => {
    expect(teamLabelForHit(hit({ teamSlug: "acme" }), [team({ name: "Acme Corp" })])).toBe(
      "Acme Corp",
    );
  });

  it("falls back to slug when nothing else is available", () => {
    expect(teamLabelForHit(hit({ teamSlug: "acme" }), [])).toBe("acme");
  });
});

describe("hitLocationLabel", () => {
  it("formats team and channel with the current name", () => {
    expect(
      hitLocationLabel(hit({ teamSlug: "acme", channelName: "general" }), [
        team({ name: "New Name" }),
      ]),
    ).toBe("New Name · #general");
  });
});
