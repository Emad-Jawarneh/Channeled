export type MoveBatchPrior = {
  id: string;
  previousFolderId: string | null;
  name?: string;
};

export type MoveBatchFailure = {
  id: string;
  name?: string;
  message: string;
};

export type MoveBatchResult = {
  successes: MoveBatchPrior[];
  failures: MoveBatchFailure[];
};

const DEFAULT_CONCURRENCY = 4;

/**
 * Concurrent move with partial-failure collection.
 * Does not throw on per-item errors; aggregates them.
 */
export async function moveBatch(opts: {
  items: MoveBatchPrior[];
  targetFolderId: string | null;
  targetChannelName?: string | null;
  moveOne: (
    id: string,
    targetFolderId: string | null,
    targetChannelName?: string | null,
  ) => Promise<unknown>;
  concurrency?: number;
  onProgress?: (done: number, total: number) => void;
}): Promise<MoveBatchResult> {
  const {
    items,
    targetFolderId,
    targetChannelName,
    moveOne,
    concurrency = DEFAULT_CONCURRENCY,
    onProgress,
  } = opts;

  const successes: MoveBatchPrior[] = [];
  const failures: MoveBatchFailure[] = [];
  let done = 0;
  let next = 0;

  async function worker() {
    while (next < items.length) {
      const index = next++;
      const item = items[index]!;
      try {
        await moveOne(item.id, targetFolderId, targetChannelName);
        successes.push(item);
      } catch (err) {
        failures.push({
          id: item.id,
          name: item.name,
          message: err instanceof Error ? err.message : "Move failed",
        });
      } finally {
        done += 1;
        onProgress?.(done, items.length);
      }
    }
  }

  const pool = Math.min(Math.max(1, concurrency), Math.max(1, items.length));
  await Promise.all(Array.from({ length: pool }, () => worker()));
  return { successes, failures };
}

export function summarizeMoveResult(result: MoveBatchResult): string {
  const ok = result.successes.length;
  const fail = result.failures.length;
  const total = ok + fail;
  if (fail === 0) return `Moved ${ok} item${ok === 1 ? "" : "s"}`;
  if (ok === 0) return `Could not move ${fail} item${fail === 1 ? "" : "s"}`;
  return `Moved ${ok} of ${total}; ${fail} failed`;
}
