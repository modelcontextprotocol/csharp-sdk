// Print the versions to build, one per line, as "<slug>\t<ref>".
// Consumed by the multi-version docs workflow to drive its build loop.
//
// Usage: node scripts/list-versions.mjs [--versions <path>]

import { readFile } from "node:fs/promises";
import path from "node:path";
import { manifestPath } from "./manifest-path.mjs";

const arg = process.argv.slice(2);
const i = arg.indexOf("--versions");
const versionsPath = i !== -1 ? path.resolve(arg[i + 1]) : manifestPath;

const manifest = JSON.parse(await readFile(versionsPath, "utf8"));
for (const v of manifest.versions) {
  process.stdout.write(`${v.slug}\t${v.ref}\n`);
}
