# Monitoring the Release PR

Opening the release PR is not the end of stage 1; it starts a watch that runs until the checks
reach a terminal state. Reporting the PR URL and stopping leaves the user to discover failures
themselves, which is exactly backwards -- the agent is the one already holding the context needed
to interpret them.

Monitoring is **automatic and read-only**. It never merges, never pushes, and never publishes.
Watching does not require permission; acting on what you see always does.

## When to start a watch

Start, or restart, monitoring:

- Immediately after the release PR is created (prepare-release Step 13).
- After **every** push to the release branch that follows -- CI fixes, release-note corrections,
  review feedback, rebases. Each push produces a new head SHA with its own set of runs.
- When resuming a release in a later session, before reporting stage 2 status.

A restart is a fresh watch against the **new head SHA**. Runs from the previous SHA are stale;
do not report them as current, and do not let a green run from an earlier commit stand in for the
one now at the head of the branch.

## Running the watch

1. Resolve the current head SHA of the release branch.
2. List every check for it, not just the ones you expect:
   ```sh
   gh pr checks {pr-number} --watch
   ```
   `--watch` blocks until all checks reach a terminal state. Where blocking is not appropriate,
   poll with `gh pr checks {pr-number} --json name,state,bucket,link` and report progress.
3. Wait for **terminal** completion. A check that is queued, in progress, or pending is not a
   result. Do not summarize a partially-complete run as passing.
4. Confirm the run set is complete. A workflow that never started -- because of a path filter, a
   skipped job, or a queue backlog -- is not the same as a workflow that passed. Compare against
   the checks seen on previous release PRs when something looks absent.

## Reporting

Report a compact per-check table plus a single overall verdict:

| Check | Result |
|---|---|
| Build / build (ubuntu-latest, net10.0) | ✅ |
| Pack / APICompat | ❌ |
| CodeQL / csharp | ✅ |
| markdown-link-check | ✅ |

**Verdict: blocked** -- Pack / APICompat failed.

Use three states and name them explicitly: **green**, **running**, **blocked**. "Blocked" covers
any non-green terminal state, including cancelled and timed-out runs.

## On failure

Diagnose before proposing anything. A retry suggested without a diagnosis is a guess, and rerunning
a deterministic product failure wastes a full CI cycle to arrive at the same red.

1. **Retrieve the logs automatically.** Do not ask the user to paste them.
   ```sh
   gh run view {run-id} --log-failed
   ```
2. **Classify the failure**, because the two classes call for opposite responses:

   | Class | Signals | Response |
   |---|---|---|
   | **Product / API validation** | ApiCompat or package validation errors, compile errors, assertion failures, behavior differences | Real. Diagnose it. Never rerun to make it go away |
   | **Infrastructure / tooling** | Runner allocation, network or feed timeouts, artifact upload, rate limits, cancelled by concurrency | A rerun is reasonable, once, with the reason stated |

   Flaky tests sit between the two. Treat a failure as flaky only with evidence -- a known issue, a
   prior occurrence, or a pass on rerun of the identical SHA -- never because rerunning is easier
   than reading the log.

3. **For ApiCompat and package validation failures specifically**, apply the interpretation rules in
   [apicompat-apidiff.md](../../skills/prepare-release/references/apicompat-apidiff.md) before
   concluding the release is breaking. `Unnecessary suppressions found` and a stale baseline
   produce large, convincing, and entirely phantom break listings.

4. **Present the diagnosis with a proposed fix, and stop.** Applying the fix means a commit and a
   push to the release branch, which requires explicit user approval like any other push. Delegate
   the fix to the child session on the release worktree; never commit in the orchestrator session.

5. After an approved fix is pushed, **restart the watch** for the new SHA without being asked.

## Stage 2 handoff

Stage 2 stays **blocked** until the checks are green, or until the user explicitly decides to
proceed anyway. Record that decision and who made it.

When handing off, lead with CI status rather than only inviting review:

> **CI: green** -- all 9 checks passed on `24c252cd`. PR #1792 is ready for your review and merge.

or

> **CI: blocked** -- Pack / APICompat failed on `6d839c6d`. Diagnosis below. PR #1792 is not ready
> to merge yet.

or

> **CI: running** -- 4 of 9 checks complete, none failed. I am still watching and will report when
> they finish.

Never say only "the PR is up, please review and merge." Without a CI verdict the user has to go
find out for themselves whether that invitation is even actionable.
