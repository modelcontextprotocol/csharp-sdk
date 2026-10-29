// Run with: node --test scripts/docs-branches.test.mjs
// Set DOCS_WORKFLOW_PATH to test another checkout's workflow with these helpers.
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

const root = fileURLToPath(new URL("../", import.meta.url));
const workflow = readFileSync(process.env.DOCS_WORKFLOW_PATH || path.join(root, ".github", "workflows", "docs.yml"), "utf8").replace(/\r\n/g, "\n");
const buildScript = workflow.match(/    - name: Build all versions\n[\s\S]*?      run: \|\n([\s\S]*?)(?=\n    - name:)/)[1]
  .split("\n").map(line => line.replace(/^        /, "")).join("\n");
const bash = process.env.BASH_PATH || (process.platform === "win32"
  ? path.join(process.env.ProgramFiles, "Git", "bin", "bash.exe") : "bash");

function fixture(t) {
  const dir = mkdtempSync(path.join(os.tmpdir(), "docs-branches-"));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const origin = path.join(dir, "origin");
  const checkout = path.join(dir, "checkout");
  const temp = path.join(dir, "runner-temp");
  mkdirSync(origin);
  mkdirSync(temp);
  const gitAt = (cwd, ...args) => execFileSync("git", args, { cwd, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
  const git = (...args) => gitAt(checkout, ...args);
  gitAt(origin, "init", "--initial-branch=main");
  gitAt(origin, "config", "user.name", "Docs tests");
  gitAt(origin, "config", "user.email", "docs-tests@example.invalid");
  gitAt(origin, "config", "commit.gpgsign", "false");
  cpSync(path.join(root, "scripts"), path.join(origin, "scripts"), { recursive: true });
  cpSync(path.join(root, "docs", "version-picker"), path.join(origin, "docs", "version-picker"), { recursive: true });
  const picker = readFileSync(path.join(root, "docs", "version-picker", "version-picker.js"), "utf8");
  const resolver = readFileSync(path.join(root, "scripts", "list-versions.mjs"), "utf8");
  function commit(version, content, marker = content) {
    writeFileSync(path.join(origin, "docs", "version-picker", "version-picker.js"), `${picker}\n// ${marker}\n`);
    writeFileSync(path.join(origin, "scripts", "list-versions.mjs"), `${resolver}\nconsole.error(${JSON.stringify(marker)});\n`);
    mkdirSync(path.join(origin, "src"), { recursive: true });
    mkdirSync(path.join(origin, "fixture-site"), { recursive: true });
    writeFileSync(path.join(origin, "src", "Directory.Build.props"),
      `<Project><PropertyGroup><VersionPrefix>${version}</VersionPrefix></PropertyGroup></Project>`);
    for (const page of ["index.html", "versioning.html"]) {
      writeFileSync(path.join(origin, "fixture-site", page), `<html><head></head><body>${content}</body></html>`);
    }
    gitAt(origin, "add", ".");
    gitAt(origin, "commit", "-m", content);
    return gitAt(origin, "rev-parse", "HEAD");
  }
  const tagSha = commit("2.3.0", "released tag content", "tag orchestration marker");
  gitAt(origin, "tag", "v2.3.0");
  gitAt(origin, "checkout", "-b", "release/2.x");
  const v2Sha = commit("2.3.1", "release/2.x branch content beyond tag");
  gitAt(origin, "checkout", "-b", "release/1.x", "main");
  const v1TagSha = commit("1.4.0", "v1 released tag content");
  gitAt(origin, "tag", "v1.4.0");
  const v1Sha = commit("1.4.1", "release/1.x branch content");
  gitAt(origin, "checkout", "main");
  const mainSha = commit("2.4.0", "main content must never ship", "main orchestration marker");
  gitAt(origin, "checkout", "-b", "docs-test");
  const manualSha = commit("2.4.0", "manual branch content must never ship", "manual orchestration marker");
  gitAt(origin, "checkout", "main");
  gitAt(dir, "clone", "--quiet", origin, checkout);
  const orchestrations = {
    release: { ref: "refs/tags/v2.3.0", sha: tagSha, marker: "tag orchestration marker" },
    workflow_dispatch: { ref: "refs/heads/docs-test", sha: manualSha, marker: "manual orchestration marker" },
  };
  const manifest = {
    default: "v2",
    versions: [
      { slug: "v2", ref: "v2.3.0", label: "v2.3.0", prerelease: false },
      { slug: "v1", ref: "v1.4.0", label: "v1.4.0", prerelease: false },
    ],
  };
  const manifestFile = path.join(temp, "docs-versions.json");
  writeFileSync(manifestFile, JSON.stringify(manifest));
  const env = { ...process.env, RUNNER_TEMP: temp };
  const resolve = (source = "latest-branches") => spawnSync(process.execPath, [path.join(checkout, "scripts", "list-versions.mjs"), "--source", source],
    { cwd: checkout, env, encoding: "utf8" });
  const build = (event, source = "release-tags") => {
    const invocation = orchestrations[event];
    // Model actions/checkout's explicit main override, not the event tag/manual ref.
    git("checkout", "--detach", mainSha);
    return spawnSync(bash, ["-c", `
    make() {
      [[ "$1" == "-C" && "$3" == "generate-docs" ]] || return 1
      mkdir -p "$2/artifacts/_site"
      cp "$2/fixture-site/"* "$2/artifacts/_site/"
    }
    ${buildScript}`], { cwd: checkout, env: { ...env, GITHUB_EVENT_NAME: event, GITHUB_REF: invocation.ref, DOCS_SOURCE: source }, encoding: "utf8" });
  };
  return { origin, checkout, temp, git, gitAt, commit, tagSha, v1TagSha, mainSha, v1Sha, v2Sha, manifest, resolve, build, orchestrations };
}

test("resolves remote release refs to full SHAs, preserving published metadata order", t => {
  const f = fixture(t);
  const result = f.resolve();
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout, `v2\trefs/remotes/origin/release/2.x\t${f.v2Sha}\nv1\trefs/remotes/origin/release/1.x\t${f.v1Sha}\n`);
  assert.notEqual(f.v2Sha, f.tagSha);
  assert.notEqual(f.v2Sha, f.mainSha);
  assert.match(result.stderr, new RegExp(`Docs content v2: refs/remotes/origin/release/2.x at ${f.v2Sha}`));
  const legacy = execFileSync(process.execPath, [path.join(f.checkout, "scripts", "list-versions.mjs")],
    { cwd: f.checkout, env: { ...process.env, RUNNER_TEMP: f.temp }, encoding: "utf8" });
  assert.equal(legacy, "v2\tv2.3.0\nv1\tv1.4.0\n");
});

test("missing branch fails despite matching main major and an existing release tag", t => {
  const f = fixture(t);
  f.git("update-ref", "-d", "refs/remotes/origin/release/2.x");
  const result = f.resolve();
  assert.notEqual(result.status, 0);
  assert.equal(result.stdout, "");
  assert.match(result.stderr, /Missing docs source refs\/remotes\/origin\/release\/2.x.*no fallback/);
});

test("major mismatch fails all resolution before emitting build rows", t => {
  const f = fixture(t);
  f.git("update-ref", "refs/remotes/origin/release/1.x", f.v2Sha);
  const result = f.resolve();
  assert.notEqual(result.status, 0);
  assert.equal(result.stdout, "");
  assert.match(result.stderr, /has major 'v2', not 'v1'/);
});

test("invalid VersionPrefix fails explicitly", t => {
  const f = fixture(t);
  const invalidSha = f.commit("invalid", "invalid version");
  f.git("fetch", "origin");
  f.git("update-ref", "refs/remotes/origin/release/2.x", invalidSha);
  const result = f.resolve();
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /unable to determine VersionPrefix/);
});

for (const [event, source] of [
  ["release", "release-tags"],
  ["workflow_dispatch", "release-tags"],
  ["workflow_dispatch", "latest-branches"],
]) {
  test(`${event}/${source} uses current main helpers/picker with selected content for every major`, t => {
    const f = fixture(t);
    const result = f.build(event, source);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const invocation = f.orchestrations[event];
    assert.ok(result.stdout.includes(`Docs orchestration: refs/heads/main at ${f.mainSha}`));
    assert.equal(f.git("rev-parse", "HEAD"), f.mainSha);
    assert.notEqual(invocation.sha, f.mainSha);
    assert.match(result.stderr, /main orchestration marker/);
    assert.doesNotMatch(result.stderr, new RegExp(invocation.marker));
    const branchMode = source === "latest-branches";
    const ref = branchMode ? "refs/remotes/origin/release/2.x" : "refs/tags/v2.3.0";
    const sha = branchMode ? f.v2Sha : f.tagSha;
    assert.ok(result.stdout.includes(`Build v2 (from ${ref} at ${sha})`));
    const combined = path.join(f.checkout, "combined");
    const v2Page = readFileSync(path.join(combined, "v2", "index.html"), "utf8");
    assert.ok(v2Page.includes(branchMode ? "release/2.x branch content beyond tag" : "released tag content"));
    assert.doesNotMatch(v2Page, /main content must never ship|manual branch content must never ship/);
    assert.ok(!v2Page.includes(branchMode ? "released tag content" : "release/2.x branch content beyond tag"));
    assert.match(v2Page, /<!--dv-picker-->/);
    assert.match(v2Page, /"label":"v2.3.0"/);
    assert.ok(readFileSync(path.join(combined, "v1", "index.html"), "utf8")
      .includes(branchMode ? "release/1.x branch content" : "v1 released tag content"));
    assert.match(readFileSync(path.join(combined, "index.html"), "utf8"), /url=.\/v2\//);
    assert.match(readFileSync(path.join(combined, "versioning.html"), "utf8"), /url=v1\/versioning.html/);
    assert.equal(readFileSync(path.join(combined, "assets", "version-picker.js"), "utf8").replace(/\r\n/g, "\n"),
      f.git("show", `${f.mainSha}:docs/version-picker/version-picker.js`) + "\n");
    assert.match(readFileSync(path.join(combined, "assets", "version-picker.js"), "utf8"), /main orchestration marker/);
    assert.deepEqual(JSON.parse(readFileSync(path.join(combined, "docs-versions.json"), "utf8")), {
      default: "v2",
      versions: f.manifest.versions.map(({ slug, label, prerelease }) => ({ slug, label, prerelease })),
    });
    assert.equal(f.git("worktree", "list", "--porcelain").match(/^worktree /gm).length, 1);
    t.diagnostic(`Output proof (${event}/${source}, fixture HTML build): main orchestration ${f.mainSha}, distinct from event ${invocation.ref} ${invocation.sha}; v2 content ${ref} ${sha}.`);
    t.diagnostic("Both major paths use the selected source; metadata labels/order remain unchanged; root/v1 redirects and main picker assets verified.");
  });
}

for (const source of ["release-tags", "latest-branches"]) {
  test(`${source} stops before building when selected ref is missing, without fallback`, t => {
    const f = fixture(t);
    if (source === "latest-branches") {
      f.gitAt(f.origin, "branch", "-D", "release/2.x");
    } else {
      f.gitAt(f.origin, "tag", "-d", "v2.3.0");
      f.git("tag", "-d", "v2.3.0");
    }
    const result = f.build("workflow_dispatch", source);
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /Missing docs source .*no fallback/);
    assert.doesNotMatch(result.stdout, /::group::Build/);
  });

  test(`${source} stops before building when selected ref major mismatches`, t => {
    const f = fixture(t);
    if (source === "latest-branches") {
      f.gitAt(f.origin, "branch", "-f", "release/1.x", f.v2Sha);
    } else {
      f.gitAt(f.origin, "tag", "-f", "v1.4.0", f.v2Sha);
      f.git("tag", "-d", "v1.4.0");
    }
    const result = f.build("workflow_dispatch", source);
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /has major 'v2', not 'v1'/);
    assert.doesNotMatch(result.stdout, /::group::Build/);
  });
}

for (const source of ["release-tags", "latest-branches"]) {
test(`${source} resolves full SHAs once and remains pinned when selected ref moves`, t => {
  const f = fixture(t);
  const result = f.resolve(source);
  assert.equal(result.status, 0, result.stderr);
  const [, ref, sha] = result.stdout.split("\n")[0].split("\t");
  assert.equal(sha, source === "latest-branches" ? f.v2Sha : f.tagSha);
  f.git("update-ref", ref, f.mainSha);
  assert.ok(f.git("show", `${sha}:fixture-site/index.html`)
    .includes(source === "latest-branches" ? "release/2.x branch content beyond tag" : "released tag content"));
});
}

test("invalid source selection fails rather than silently defaulting", t => {
  const f = fixture(t);
  const result = f.resolve("invalid");
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Invalid docs source/);
});

test("release-tag resolution does not require release branches", t => {
  const f = fixture(t);
  f.git("update-ref", "-d", "refs/remotes/origin/release/1.x");
  f.git("update-ref", "-d", "refs/remotes/origin/release/2.x");
  const result = f.resolve("release-tags");
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout, `v2\trefs/tags/v2.3.0\t${f.tagSha}\nv1\trefs/tags/v1.4.0\t${f.v1TagSha}\n`);
});

test("annotated release tags resolve to the full content commit SHA", t => {
  const f = fixture(t);
  f.gitAt(f.origin, "tag", "-f", "-a", "v2.3.0", f.tagSha, "-m", "annotated fixture tag");
  f.git("tag", "-d", "v2.3.0");
  f.git("fetch", "--tags", "origin");
  const result = f.resolve("release-tags");
  assert.equal(result.status, 0, result.stderr);
  assert.ok(result.stdout.startsWith(`v2\trefs/tags/v2.3.0\t${f.tagSha}\n`));
  assert.notEqual(f.git("rev-parse", "refs/tags/v2.3.0"), f.tagSha);
});

test("workflow restores all-major source choice, tag default, main checkout and Pages safeguards", () => {
  assert.match(workflow, /release:\s+types: \[published\]/);
  assert.match(workflow, /workflow_dispatch:\s*\n/);
  const checkout = workflow.match(/    - name: Checkout docs orchestration\n([\s\S]*?)(?=\n    - name:)/)[1];
  assert.match(checkout, /^\s+ref: main$/m);
  assert.match(workflow, /docs_source:\s+description: Source for every published major-version path\s+required: true\s+default: release-tags\s+type: choice\s+options:\s+- release-tags\s+- latest-branches/);
  assert.ok(workflow.includes("DOCS_SOURCE: ${{ inputs.docs_source || 'release-tags' }}"));
  assert.match(buildScript, /--source "\$DOCS_SOURCE"/);
  assert.equal(buildScript.match(/git worktree add/g).length, 1);
  assert.match(buildScript, /git worktree add --detach "\$wt" "\$sha"/);
  assert.match(workflow, /contents: read\s+pages: write\s+id-token: write/);
  assert.match(workflow, /group: "pages"\s+cancel-in-progress: true/);
});
