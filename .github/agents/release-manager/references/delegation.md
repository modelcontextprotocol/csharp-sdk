# Delegation and Worktrees

The release-manager session is an **orchestrator**. It stays on whatever branch it started on and
never checks out or mutates a release branch. Work that creates commits happens in a **child session
on its own worktree**, based on the target release branch.

This mirrors how [`docs.yml`](../../workflows/docs.yml) already works: the orchestration scripts run
from a single fixed checkout, while each version's content is built from its own tag in a separate
worktree.

## Why

- **Current orchestration.** The agent runs from the checkout it was launched in, so a servicing
  release for an older branch still uses the process as it exists in that checkout, not the process
  as it existed when the release branch forked.
- **A clean working tree.** The orchestrator holds long-lived session state -- stage timings, gate
  interactions, the progress rail. Checking out branches underneath it risks losing that context
  and makes "which branch am I on?" a source of error at exactly the moment precision matters.
- **Isolation of the risky part.** Only stage 1 writes to the repository. Confining it to a
  disposable worktree means an abandoned or failed preparation leaves the orchestrator's branch
  untouched.
- **Concurrency.** A `2.0.0-preview.2` preparation and a `1.3.1` servicing preparation can proceed
  independently, each in its own worktree.

## What runs where

| Stage | Mutates the repo? | Runs where |
|---|---|---|
| 1. Prepare | **Yes** -- version bump, suppressions, docs, commit, branch, PR | **Child session** on a worktree based on the source/base branch |
| 2. Review and merge | No -- reads CI and PR state | Orchestrator, in place |
| 3. Publish | No -- reads merged PR, writes only a GitHub draft release | Orchestrator, in place |
| 4. Release | No -- human action in the GitHub UI | Orchestrator, in place |
| 5. Verify | No -- reads workflow runs and published artifacts | Orchestrator, in place |

Stage 3 does edit `src/PACKAGE.md` and `README.md` when the README checklist finds issues. Those
fixes land on the release branch, so delegate them the same way as stage 1: a child session on a
worktree based on the branch the draft release targets.

## Confirm the orchestrator's location

Before starting any stage, note the branch this session started on and confirm the working tree is
clean. Stay on that branch for the whole release -- do not switch branches to match the release.

- **Dirty working tree** -- report the uncommitted changes and ask how to proceed. Do not stash,
  reset, or commit unrelated work.
- **Session started on a release branch** -- that is fine; the orchestrator only reads. Still
  delegate stage 1 to a worktree rather than committing in place.

A status assessment is read-only and is safe from anywhere; say so rather than blocking the user on
a technicality.

## Delegating stage 1

Create the child session with the **source/base branch** selected in prepare-release Step 1 as its
base -- `main` or `release/{MAJOR}.x`. The child creates the `release-{version}` work branch itself,
as part of the skill's Step 6. Do not create that branch yourself, and do not pass it as the base.

The child's kickoff prompt must carry everything it needs, because it does not share your context:

1. The instruction to run the **prepare-release** skill.
2. The source/base branch, already selected.
3. The target commit or ref, if the user chose one.
4. Any decisions the user has already made -- the confirmed version, breaking-change conclusions,
   or a chosen preamble -- so the child does not re-litigate them.
5. The requirement to **stop at the skill's Step 12 gate** and report back rather than pushing or
   creating the PR.

If app-native child sessions are not available in the current environment, fall back to a git
worktree created from the source/base branch and run the skill there, keeping the orchestrator's
own checkout untouched. The invariant is the worktree, not the mechanism.

## Gates stay with the orchestrator

The human gates belong to the orchestrator session. The child prepares and reports; the user
approves in the conversation they are already having with you; you relay the approval.

Never let the child push a branch, open a PR, or create a release on its own initiative. When the
child reaches Step 12, it reports the full release summary back to you, you present that to the
user with the progress rail, and only after explicit approval do you instruct the child to proceed
with Step 13.

## Timing across sessions

Session tracking stays in the **orchestrator**. A stage delegated to a child is still one stage on
your timeline: record `started_at` when you dispatch the child, and `ended_at` when its gate is
satisfied.

Time the child spends working is **wait time**, not interaction time -- the user is not answering
prompts while the child builds and packs. Time the user spends reviewing what the child reported
**is** interaction time. See [session-tracking.md](session-tracking.md).

## Cleaning up

When a release is complete, offer to remove the worktrees created for it. If a preparation was
abandoned, say the worktree and its `release-{version}` branch still exist and offer to remove
them. Never remove a worktree with uncommitted changes without showing the user what would be lost.
