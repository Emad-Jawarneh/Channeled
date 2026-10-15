import type { Page } from "@playwright/test";
import {
  Kind,
  createChannel,
  createTeam,
  expect,
  postMessage,
  test,
  uploadMany,
  type Person,
  type TeamRef,
} from "./fixtures";

/**
 * Report-only performance probe (docs/DECISIONS.md): records long tasks and interaction latency on big
 * DOMs. Virtualization is only justified when interaction p95 exceeds 100 ms here. Results are attached
 * to the test and printed; the probe never fails on timings.
 */
const INTERACTION_P95_BUDGET_MS = 100;

/** `events`: total durations; `processing`: JS handler time; `presentation`: time after handlers until paint. */
type Probe = { longTasks: number[]; events: number[]; processing: number[]; presentation: number[] };

async function installProbe(page: Page) {
  await page.addInitScript(() => {
    const store: Probe = { longTasks: [], events: [], processing: [], presentation: [] };
    (window as unknown as { __probe: Probe }).__probe = store;
    try {
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) store.longTasks.push(entry.duration);
      }).observe({ type: "longtask", buffered: true });
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries() as PerformanceEventTiming[]) {
          store.events.push(entry.duration);
          store.processing.push(entry.processingEnd - entry.processingStart);
          store.presentation.push(entry.startTime + entry.duration - entry.processingEnd);
        }
      }).observe({ type: "event", buffered: true, durationThreshold: 16 } as PerformanceObserverInit);
    } catch {
      /* Observers unsupported: the report shows empty arrays. */
    }
  });
}

async function readProbe(page: Page): Promise<Probe> {
  return page.evaluate(() => {
    const p = (window as unknown as { __probe: Probe }).__probe;
    const copy = {
      longTasks: [...p.longTasks],
      events: [...p.events],
      processing: [...p.processing],
      presentation: [...p.presentation],
    };
    p.longTasks.length = 0;
    p.events.length = 0;
    p.processing.length = 0;
    p.presentation.length = 0;
    return copy;
  });
}

function p95(values: number[]): number {
  if (values.length === 0) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  return Math.round(sorted[Math.min(sorted.length - 1, Math.ceil(0.95 * sorted.length) - 1)]!);
}

function report(name: string, data: Record<string, unknown>) {
  const body = JSON.stringify(data, null, 2);
  test.info().annotations.push({ type: "perf", description: `${name}: ${body}` });
  console.log(`[perf-probe] ${name}\n${body}`);
}

let owner: Person;
let team: TeamRef;
const MESSAGES = 1_000;
const FILES = 2_000;

test.describe("perf probe (report-only)", () => {
  test.beforeAll(async ({ people }) => {
    test.setTimeout(300_000);
    owner = await people.signUp("Perf");
    team = await createTeam(owner);
    await createChannel(owner, team.slug, "busy", { kind: Kind.Text });
    let next = 0;
    const worker = async () => {
      while (next < MESSAGES) {
        const i = next++;
        await postMessage(owner, team.slug, "busy", `Message ${i} — the quick brown fox jumps over the lazy dog.`);
      }
    };
    await Promise.all(Array.from({ length: 8 }, worker));
    await createChannel(owner, team.slug, "bulk");
    await uploadMany(owner, team.slug, "bulk", FILES, (i) => `row-${String(i).padStart(4, "0")}.txt`, { concurrency: 16 });
  });

  test("file list with 1,000 and 2,000 loaded rows", async ({ openAs }) => {
    test.setTimeout(300_000);
    const page = await openAs(owner);
    await installProbe(page);
    await page.goto(`/t/${team.slug}/c/bulk`);
    const rows = page.locator("[data-testid^='row-row-']");
    await expect(rows).toHaveCount(50);

    const results: Record<string, unknown> = {};
    for (const target of [1_000, 2_000]) {
      const loads: number[] = [];
      while ((await rows.count()) < target) {
        const before = await rows.count();
        const started = Date.now();
        await page.getByTestId("load-more").click();
        await expect.poll(() => rows.count()).toBeGreaterThan(before);
        loads.push(Date.now() - started);
      }
      const loading = await readProbe(page);

      // Interactions on the full table: select rows, then tick checkboxes (measured separately).
      const indexes = [10, Math.floor(target / 2), target - 5];
      for (const index of indexes) {
        const row = rows.nth(index);
        await row.scrollIntoViewIfNeeded();
        await row.click();
      }
      const selecting = await readProbe(page);
      for (const index of indexes) {
        await rows.nth(index).locator("input[type=checkbox]").check();
      }
      const ticking = await readProbe(page);
      const all = [...selecting.events, ...ticking.events];
      const interactionP95 = p95(all);
      results[`rows${target}`] = {
        rows: await rows.count(),
        loadMoreP95Ms: p95(loads),
        longTasksWhileLoading: { count: loading.longTasks.length, maxMs: Math.round(Math.max(0, ...loading.longTasks)) },
        interactionP95Ms: interactionP95,
        selectP95Ms: p95(selecting.events),
        checkP95Ms: p95(ticking.events),
        processingP95Ms: p95([...selecting.processing, ...ticking.processing]),
        presentationP95Ms: p95([...selecting.presentation, ...ticking.presentation]),
        overBudget: interactionP95 > INTERACTION_P95_BUDGET_MS,
      };
    }
    report("file-list", { ...results, budgetMs: INTERACTION_P95_BUDGET_MS });
  });

  test("chat with 1,000 messages", async ({ openAs }) => {
    test.setTimeout(300_000);
    const page = await openAs(owner);
    await installProbe(page);
    await page.goto(`/t/${team.slug}/c/busy`);
    const messages = page.getByTestId("chat-message");
    await expect(messages).toHaveCount(50);

    const pageLoads: number[] = [];
    while ((await messages.count()) < MESSAGES) {
      const before = await messages.count();
      const started = Date.now();
      await page.getByTestId("chat-load-older").click();
      await expect.poll(() => messages.count()).toBeGreaterThan(before);
      pageLoads.push(Date.now() - started);
    }
    const loading = await readProbe(page);

    // Interactions on the full DOM: hover a message, open reply, type in the composer.
    const target = messages.nth(500);
    await target.scrollIntoViewIfNeeded();
    await target.hover();
    await target.getByTestId("chat-reply").click();
    await page.getByTestId("chat-input").pressSequentially("typing on a big log", { delay: 20 });
    const interacting = await readProbe(page);

    const interactionP95 = p95(interacting.events);
    report("chat-1000", {
      messages: await messages.count(),
      loadOlderP95Ms: p95(pageLoads),
      longTasksWhileLoading: { count: loading.longTasks.length, maxMs: Math.round(Math.max(0, ...loading.longTasks)) },
      interactionEventsOver16ms: interacting.events.length,
      interactionP95Ms: interactionP95,
      budgetMs: INTERACTION_P95_BUDGET_MS,
      overBudget: interactionP95 > INTERACTION_P95_BUDGET_MS,
    });
  });
});
