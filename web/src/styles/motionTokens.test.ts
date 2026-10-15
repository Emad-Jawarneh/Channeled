import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

// Vitest serves CSS imports as empty strings, so read the file from disk (cwd is the web/ project root).
const tokensCss = readFileSync("src/styles/tokens.css", "utf8");

describe("motion tokens", () => {
  it("defines duration and easing variables", () => {
    expect(tokensCss).toMatch(/--dur-micro:\s*90ms/);
    expect(tokensCss).toMatch(/--dur-fast:\s*120ms/);
    expect(tokensCss).toMatch(/--dur-base:\s*200ms/);
    expect(tokensCss).toMatch(/--ease-out:\s*cubic-bezier/);
    expect(tokensCss).toMatch(/--ease-soft:\s*cubic-bezier/);
  });

  it("respects prefers-reduced-motion", () => {
    expect(tokensCss).toContain("@media (prefers-reduced-motion: reduce)");
    expect(tokensCss).toContain("animation-duration: 0.01ms !important");
    expect(tokensCss).toContain("transition-duration: 0.01ms !important");
  });

  it("provides transform/opacity keyframes only (no height/width/top/left)", () => {
    const blocks = tokensCss.match(/@keyframes\s+[\w-]+\s*\{[\s\S]*?\n\}/g) ?? [];
    expect(blocks.length).toBeGreaterThan(0);
    for (const block of blocks) {
      expect(block).not.toMatch(/\b(height|width|top|left)\s*:/);
    }
  });

  it("defines skeleton shimmer helpers", () => {
    expect(tokensCss).toContain(".skeleton");
    expect(tokensCss).toContain("motion-shimmer");
  });
});
