// Print the versions to build, one per line, as "<slug>\t<ref>".
// Consumed by the multi-version docs workflow to drive its build loop.
//
// Usage: node scripts/list-versions.mjs [--versions <path>]
// With --source release-tags|latest-branches, validate selected refs and print
// "<slug>\t<content-ref>\t<full-sha>" instead of release metadata.

import { execFileSync } from "node:child_process";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { manifestPath } from "./manifest-path.mjs";
import { getDocsVersionSlug } from "./get-docs-version-slug.mjs";

const arg = process.argv.slice(2);
const i = arg.indexOf("--versions");
const versionsPath = i !== -1 ? path.resolve(arg[i + 1]) : manifestPath;
const sourceIndex = arg.indexOf("--source");
const source = sourceIndex === -1 ? undefined : arg[sourceIndex + 1];
if (sourceIndex !== -1 && !["release-tags", "latest-branches"].includes(source)) {
  throw new Error(`Invalid docs source: ${source}; expected release-tags or latest-branches.`);
}

const manifest = JSON.parse(await readFile(versionsPath, "utf8"));
const rows = [];
for (const v of manifest.versions) {
  if (!source) {
    rows.push(`${v.slug}\t${v.ref}`);
    continue;
  }

  if (!/^v[1-9][0-9]*$/.test(v.slug)) {
    throw new Error(`Invalid docs major slug: ${v.slug}`);
  }
  if (source === "release-tags" && !new RegExp(`^${v.slug}\\.`).test(v.ref)) {
    throw new Error(`Release tag '${v.ref}' does not match major '${v.slug}'.`);
  }
  const ref = source === "release-tags"
    ? `refs/tags/${v.ref}`
    : `refs/remotes/origin/release/${v.slug.slice(1)}.x`;
  let sha;
  try {
    sha = execFileSync("git", ["rev-parse", "--verify", `${ref}^{commit}`], { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
  } catch (error) {
    throw new Error(`Missing docs source ${ref} for ${v.slug}; no fallback to another source or main.`, { cause: error });
  }
  const props = execFileSync("git", ["show", `${sha}:src/Directory.Build.props`], { encoding: "utf8" });
  const sourceSlug = getDocsVersionSlug(props, `${ref} (${sha})`);
  if (sourceSlug !== v.slug) {
    throw new Error(`Source '${ref}' (${sha}) has major '${sourceSlug}', not '${v.slug}'.`);
  }
  console.error(`Docs content ${v.slug}: ${ref} at ${sha}`);
  rows.push(`${v.slug}\t${ref}\t${sha}`);
}
process.stdout.write(rows.join("\n") + (rows.length ? "\n" : ""));
