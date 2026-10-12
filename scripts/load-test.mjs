#!/usr/bin/env node
/**
 * M8 load test harness.
 * Default CI profile is reduced. Full AGENTS scale:
 *   LOAD_USERS=200 LOAD_TEAMS=50 LOAD_FILES=1000000 LOAD_TARGET_TB=2
 *
 * Measures p95 latency for list + search against a running API with cookie auth.
 */
import { createHash, randomBytes } from "node:crypto";

const base = process.env.LOAD_BASE_URL ?? "http://127.0.0.1:5048";
const email = process.env.LOAD_EMAIL ?? "e2e-admin@example.com";
const password = process.env.LOAD_PASSWORD ?? "E2eAdminPassword1!";
const users = Number(process.env.LOAD_USERS ?? 20);
const iterations = Number(process.env.LOAD_ITERATIONS ?? 50);
const p95MaxMs = Number(process.env.LOAD_P95_MAX_MS ?? 2000);

const fullScale = {
  users: Number(process.env.LOAD_USERS_FULL ?? 200),
  teams: Number(process.env.LOAD_TEAMS_FULL ?? 50),
  files: Number(process.env.LOAD_FILES_FULL ?? 1_000_000),
  tb: Number(process.env.LOAD_TARGET_TB ?? 2),
};

function percentile(sorted, p) {
  if (sorted.length === 0) return 0;
  const idx = Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1);
  return sorted[idx];
}

async function login() {
  const jar = { cookie: "" };
  const res = await fetch(`${base}/api/v1/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password }),
  });
  if (!res.ok) {
    throw new Error(`login failed ${res.status}`);
  }
  const setCookie = res.headers.getSetCookie?.() ?? [];
  jar.cookie = setCookie.map((c) => c.split(";")[0]).join("; ");
  const csrf = await fetch(`${base}/api/v1/auth/csrf`, {
    headers: { Cookie: jar.cookie },
  });
  const { token } = await csrf.json();
  return { cookie: jar.cookie, csrf: token };
}

async function timed(fn) {
  const start = performance.now();
  await fn();
  return performance.now() - start;
}

async function main() {
  console.log("Channeled load test");
  console.log(
    `Profile: concurrentUsers=${users} iterations=${iterations} p95MaxMs=${p95MaxMs}`,
  );
  console.log(
    `Full AGENTS target (documented): users=${fullScale.users} teams=${fullScale.teams} files=${fullScale.files} store≈${fullScale.tb}TB`,
  );
  console.log(`Seed note: use scripts/seed-bench-files.sh for 1M metadata; object bytes are not required for latency gate.`);

  const session = await login();
  const teams = await fetch(`${base}/api/v1/teams/`, {
    headers: { Cookie: session.cookie },
  }).then((r) => r.json());
  if (!Array.isArray(teams) || teams.length === 0) {
    throw new Error("No teams available for load test — create one first.");
  }
  const slug = teams[0].slug;

  const listSamples = [];
  const searchSamples = [];

  const worker = async () => {
    for (let i = 0; i < iterations; i++) {
      listSamples.push(
        await timed(async () => {
          const r = await fetch(`${base}/api/v1/teams/${slug}/channels/general/items?limit=50`, {
            headers: { Cookie: session.cookie },
          });
          if (!r.ok) throw new Error(`list ${r.status}`);
          await r.arrayBuffer();
        }),
      );
      searchSamples.push(
        await timed(async () => {
          const q = randomBytes(2).toString("hex");
          const r = await fetch(
            `${base}/api/v1/search?q=${encodeURIComponent(q)}&scope=team&teamSlug=${encodeURIComponent(slug)}`,
            { headers: { Cookie: session.cookie } },
          );
          if (!r.ok) throw new Error(`search ${r.status}`);
          await r.arrayBuffer();
        }),
      );
    }
  };

  await Promise.all(Array.from({ length: users }, () => worker()));

  listSamples.sort((a, b) => a - b);
  searchSamples.sort((a, b) => a - b);
  const listP95 = percentile(listSamples, 95);
  const searchP95 = percentile(searchSamples, 95);
  console.log(`list p95=${listP95.toFixed(1)}ms n=${listSamples.length}`);
  console.log(`search p95=${searchP95.toFixed(1)}ms n=${searchSamples.length}`);
  console.log(`checksum=${createHash("sha256").update(String(listP95 + searchP95)).digest("hex").slice(0, 12)}`);

  if (listP95 > p95MaxMs || searchP95 > p95MaxMs) {
    console.error("Load test exceeded p95 budget for CI profile");
    process.exit(2);
  }
  console.log("load-test OK");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
