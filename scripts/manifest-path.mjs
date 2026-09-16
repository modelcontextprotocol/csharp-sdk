// Location of the docs-versions manifest.
//
// This manifest is a build-time artifact produced during docs publishing, which
// only ever runs in CI -- never on a developer's machine. It therefore lives in
// the runner's temporary directory (RUNNER_TEMP on GitHub Actions, falling back
// to the OS temp dir for local testing) so it never touches the repository tree
// and needs no .gitignore entry. Every script agrees on this path, so the
// workflow does not need to thread it between steps.

import os from "node:os";
import path from "node:path";

export const manifestPath = path.join(
  process.env.RUNNER_TEMP || os.tmpdir(),
  "docs-versions.json"
);
