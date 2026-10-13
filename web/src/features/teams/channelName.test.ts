import { describe, expect, it } from "vitest";
import { CHANNEL_NAME_MAX, validateChannelName } from "./channelName";

describe("validateChannelName", () => {
  it("requires a non-empty name", () => {
    expect(validateChannelName("")).toMatch(/required/i);
    expect(validateChannelName("   ")).toMatch(/required/i);
  });

  it("enforces max length", () => {
    expect(validateChannelName("a".repeat(CHANNEL_NAME_MAX + 1))).toMatch(/at most/i);
  });

  it("accepts a normal name", () => {
    expect(validateChannelName(" brand-assets ")).toBeNull();
  });
});
