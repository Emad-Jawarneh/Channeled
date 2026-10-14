// @ts-nocheck — Node fs used only in Vitest; app tsconfig omits @types/node.
import { readFileSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";
import { describe, expect, it } from "vitest";

const tokensCss = readFileSync(join(dirname(fileURLToPath(import.meta.url)), "tokens.css"), "utf8");

describe("motion tokens", () => {
  it("defines duration and easing variables", () => {
    expect(tokensCss).toMatch(/--dur-fast:\s*120ms/);
    expect(tokensCss).toMatch(/--dur-base:\s*200ms/);
    expect(tokensCss).toMatch(/--ease-out:\s*cubic-bezier/);
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
