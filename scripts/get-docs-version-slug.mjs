// Print the docs URL slug for a source tree's VersionPrefix.
//
// Usage:
//   node scripts/get-docs-version-slug.mjs <src/Directory.Build.props>

import { readFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

export function getDocsVersionSlug(props, source) {
  const match = props.match(/<VersionPrefix>\s*([1-9][0-9]*)\.\d+\.\d+\s*<\/VersionPrefix>/);
  if (!match) {
    throw new Error(`unable to determine VersionPrefix from ${source}`);
  }
  return `v${match[1]}`;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const propsPath = process.argv[2];
  if (!propsPath) {
    console.error("usage: get-docs-version-slug.mjs <src/Directory.Build.props>");
    process.exit(2);
  }

  process.stdout.write(`${getDocsVersionSlug(await readFile(propsPath, "utf8"), propsPath)}\n`);
}
