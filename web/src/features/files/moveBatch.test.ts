import { describe, expect, it } from "vitest";
import { moveBatch, summarizeMoveResult } from "./moveBatch";

describe("moveBatch", () => {
  it("collects partial failures and keeps concurrency capped", async () => {
    let concurrent = 0;
    let maxConcurrent = 0;
    const result = await moveBatch({
      items: [
        { id: "1", previousFolderId: null, name: "a" },
        { id: "2", previousFolderId: null, name: "b" },
        { id: "3", previousFolderId: null, name: "c" },
        { id: "4", previousFolderId: null, name: "d" },
        { id: "5", previousFolderId: null, name: "e" },
      ],
      targetFolderId: "folder",
      concurrency: 2,
      moveOne: async (id) => {
        concurrent += 1;
        maxConcurrent = Math.max(maxConcurrent, concurrent);
        await new Promise((r) => setTimeout(r, 10));
        concurrent -= 1;
        if (id === "2" || id === "4") {
          throw new Error("conflict");
        }
      },
    });

    expect(maxConcurrent).toBeLessThanOrEqual(2);
    expect(result.successes.map((s) => s.id).sort()).toEqual(["1", "3", "5"]);
    expect(result.failures.map((f) => f.id).sort()).toEqual(["2", "4"]);
    expect(summarizeMoveResult(result)).toBe("Moved 3 of 5; 2 failed");
  });
});
