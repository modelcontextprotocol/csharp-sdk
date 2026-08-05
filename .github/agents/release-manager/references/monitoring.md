# Monitoring

Two things in this process are easy to hand off passively and should not be: the release PR after
it is opened, and the draft release after it is created. In both cases the agent has the context
needed to interpret what happens next, and the user should not have to come back and report an
outcome the agent could have observed.

Monitoring is **automatic and read-only**. It never merges, never pushes, and never publishes.
Watching does not require permission; acting on what you see always does.

## Monitoring the release PR

Opening the release PR ends stage 1 and immediately begins stage 2, which owns the watch: it runs
until every check reaches a terminal state. Reporting the PR URL and stopping leaves the user to
discover failures themselves, which is exactly backwards.

Record the time accordingly. Stage 1 ends when the PR is created, and the CI watch that follows --
including any red checks, corrective pushes, and re-runs -- belongs to stage 2. Attributing that
time to stage 1 makes preparation look expensive and review look cheap, which is the opposite of
what the summary should reveal.

### When to start a watch

Start, or restart, monitoring:

- Immediately after the release PR is created (prepare-release Step 13).
- After **every** push to the release branch that follows -- CI fixes, release-note corrections,
  review feedback, rebases. Each push produces a new head SHA with its own set of runs.
- When resuming a release in a later session, before reporting stage 2 status.

A restart is a fresh watch against the **new head SHA**. Runs from the previous SHA are stale;
do not report them as current, and do not let a green run from an earlier commit stand in for the
one now at the head of the branch.

### Running the watch

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

### Reporting

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

### On failure

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

### Stage 2 handoff

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

## Monitoring the draft release

Creating the draft release ends stage 3. Stage 4 is a human action in the GitHub UI, and the
temptation is to hand off and wait to be told it happened. Do not. Publishing is the moment the
release becomes irreversible and the moment two workflows start, so it is the least useful point in
the process to be uninformed about.

Watch the release until it is no longer a draft:

```sh
gh release view v{version} --json isDraft,publishedAt,tagName,isPrerelease
```

Poll at a modest interval. This gate is human-paced and may sit for hours or span a session, so
prefer periodic checks over a tight loop, and say that you are watching rather than going silent.

**`isDraft: false` is the trigger.** The moment it flips:

1. Record the stage 4 end time from `publishedAt`, not from when you noticed. The user published
   when they published; polling latency is yours, not theirs, and it should not inflate the stage
   duration in the closing summary.
2. Confirm the details that were the user's to choose and cannot be inferred: the tag actually
   created, and whether the release was marked as a prerelease. A stable release mistakenly left
   unflagged, or a prerelease flagged as stable, changes what consumers receive.
3. **Begin stage 5 immediately** via the verify-release skill. Publishing starts the Release and
   Publish Docs workflows in parallel right away; waiting to be told to verify means arriving after
   the interesting part. Announce the transition rather than asking permission -- stage 5 is
   read-only, and the irreversible act has already occurred.

### What else the watch can find

Not every change to the draft means it was published, and the difference matters:

| Observation | Meaning | Response |
|---|---|---|
| `isDraft: false` | Published | Start stage 5 |
| Still a draft, body changed | The user is editing the notes, possibly removing the AI disclosure | Nothing. Do not re-add anything they removed |
| Draft no longer exists | Deleted, or published under a different tag | Check for a published release before assuming it was abandoned; ask |
| Published with an unexpected tag | The tag differs from the prepared version | Stop and confirm before verifying. Verifying the wrong version is worse than not verifying |

If the user says they published but the API still reports a draft, trust the API and say so plainly
-- an unsaved draft or a failed publish looks identical to success from the browser.

### Stage 4 handoff

Hand off with the action and the watch, so the user knows they do not need to come back and report:

> The draft release for **v2.1.0** is ready. Review the notes line by line, set the prerelease flag
> if applicable, and click **Publish release**. Once you have signed off you may remove the AI
> disclosure from the notes.
>
> I am watching for publication and will start verification automatically when it happens.
