// Inject the version-picker widget into every .html page of a built docs site.
//
// Usage:
//   node scripts/inject-version-picker.mjs <siteDir> <slug> [--base /] [--versions <path>]
//
// <siteDir> is a single version's built output (e.g. combined/2.0).
// <slug>    is that version's slug (must match an entry in docs-versions.json).
// The widget config is derived from docs-versions.json and written into each page's
// <head>. The picker assets are referenced relative to each generated page so they
// work from both a custom domain and a project Pages subpath.

import { readFile, writeFile, readdir } from "node:fs/promises";
import { createHash } from "node:crypto";
import path from "node:path";
import { manifestPath } from "./manifest-path.mjs";

const MARKER = "<!--dv-picker-->";

function parseArgs(argv) {
  const positional = [];
  const opts = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith("--")) opts[a.slice(2)] = argv[++i];
    else positional.push(a);
  }
  return { positional, opts };
}

function normalizeBase(base) {
  let b = (base || "/").trim();
  if (!b.startsWith("/")) b = "/" + b;
  if (!b.endsWith("/")) b += "/";
  return b.replace(/\/{2,}/g, "/");
}

async function* htmlFiles(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) yield* htmlFiles(full);
    else if (entry.isFile() && /\.html?$/i.test(entry.name)) yield full;
  }
}

async function assetRevision(name) {
  const contents = await readFile(new URL(`../docs/version-picker/${name}`, import.meta.url));
  return createHash("sha256").update(contents).digest("hex").slice(0, 12);
}

async function main() {
  const { positional, opts } = parseArgs(process.argv.slice(2));
  const [siteDir, slug] = positional;
  if (!siteDir || !slug) {
    console.error("usage: inject-version-picker.mjs <siteDir> <slug> [--base /] [--versions <path>]");
    process.exit(2);
  }

  const base = normalizeBase(opts.base);
  const versionsPath = opts.versions
    ? path.resolve(opts.versions)
    : manifestPath;
  const manifest = JSON.parse(await readFile(versionsPath, "utf8"));

  if (!manifest.versions.some((v) => v.slug === slug)) {
    console.error(`error: slug "${slug}" is not present in docs-versions.json`);
    process.exit(1);
  }

  const config = {
    version: slug,
    base,
    default: manifest.default,
    versions: manifest.versions.map((v) => ({
      slug: v.slug,
      label: v.label,
      prerelease: !!v.prerelease,
    })),
  };

  const json = JSON.stringify(config).replace(/</g, "\\u003c");
  const [cssRevision, jsRevision] = await Promise.all([
    assetRevision("version-picker.css"),
    assetRevision("version-picker.js"),
  ]);

  let injected = 0;
  let skipped = 0;
  const assetsDir = path.resolve(siteDir, "..", "assets");
  for await (const file of htmlFiles(siteDir)) {
    const html = await readFile(file, "utf8");
    if (html.includes(MARKER)) {
      skipped++;
      continue;
    }
    const idx = html.search(/<\/head>/i);
    if (idx === -1) {
      skipped++;
      continue;
    }
    const assetBase = path.relative(path.dirname(file), assetsDir).split(path.sep).join("/") + "/";
    const snippet =
      `\n${MARKER}\n` +
      `<script>window.__DOCS__=${json};</script>\n` +
      `<link rel="stylesheet" href="${assetBase}version-picker.css?v=${cssRevision}">\n` +
      `<script type="module" src="${assetBase}version-picker.js?v=${jsRevision}"></script>\n`;
    await writeFile(file, html.slice(0, idx) + snippet + html.slice(idx));
    injected++;
  }

  console.log(`[inject] ${slug}: injected into ${injected} page(s), skipped ${skipped}`);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
