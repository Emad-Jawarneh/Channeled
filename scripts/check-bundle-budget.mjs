#!/usr/bin/env node
/**
 * Fail if the main Vite JS chunk exceeds the documented ceiling (bytes, gzip not required).
 * Ceiling chosen with headroom above current ~550KB minified main chunk.
 */
import { readdirSync, statSync } from "node:fs";
import { join } from "node:path";

const ROOT = new URL("../web/dist/assets/", import.meta.url);
const MAX_MAIN_JS_BYTES = 750_000; // 750 KB — document in DECISIONS if raised

const dir = ROOT.pathname.startsWith("/") && process.platform === "win32"
  ? ROOT.pathname.slice(1)
  : ROOT.pathname;

let assets;
try {
  assets = readdirSync(dir).filter((f) => /^index-.*\.js$/.test(f));
} catch (e) {
  console.error("check-bundle-budget: dist/assets missing — run vite build first", e.message);
  process.exit(1);
}

if (assets.length === 0) {
  console.error("check-bundle-budget: no index-*.js chunk found");
  process.exit(1);
}

let worst = { name: "", size: 0 };
for (const name of assets) {
  const size = statSync(join(dir, name)).size;
  if (size > worst.size) worst = { name, size };
}

console.log(`Main chunk ${worst.name}: ${worst.size} bytes (max ${MAX_MAIN_JS_BYTES})`);
if (worst.size > MAX_MAIN_JS_BYTES) {
  console.error("Bundle budget exceeded");
  process.exit(1);
}
console.log("Bundle budget OK");
