// Print the docs URL slug for a source tree's VersionPrefix.
//
// Usage:
//   node scripts/get-docs-version-slug.mjs <src/Directory.Build.props>

import { readFile } from "node:fs/promises";

const propsPath = process.argv[2];
if (!propsPath) {
  console.error("usage: get-docs-version-slug.mjs <src/Directory.Build.props>");
  process.exit(2);
}

const props = await readFile(propsPath, "utf8");
const match = props.match(/<VersionPrefix>\s*([1-9][0-9]*)\.\d+\.\d+\s*<\/VersionPrefix>/);
if (!match) {
  console.error(`error: unable to determine VersionPrefix from ${propsPath}`);
  process.exit(1);
}

process.stdout.write(`v${match[1]}\n`);
