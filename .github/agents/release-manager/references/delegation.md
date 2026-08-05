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

Stage 3 does edit `src/PACKAGE.md` and `README.md` when the README checklist finds issues. **The
release branch is already merged by this point, so those fixes cannot land on it.** They go to the
base branch the release ships from — `main` or `release/{MAJOR}.x` — which is protected, so they
need their own small PR, reviewed and merged like any other change.

Delegate that PR the same way as stage 1: a child session on a fresh worktree based on the base
branch. Do not push directly to the base branch, and do not commit into the orchestrator's worktree.

A corrective commit merged at this point **is not in the draft release's tag**, because the draft is
pinned to the merge commit the user approved. After the fix merges, re-target the draft to the new
head and regenerate the notes per
[publish-release Step 9](../../../skills/publish-release/SKILL.md). Skipping the re-target ships a
tag that predates the fix while the notes describe the fixed state.

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

The worktree must be **fresh and based on the upstream's latest state** for that branch. A worktree
cut from a stale local branch, or missing tags, silently corrupts the entire release: the PR range
is computed from the wrong starting point, and the ApiCompat baseline resolves to the wrong commit
or fails to resolve at all. Before the child begins Step 1, it must complete prepare-release
**Step 0**: identify the upstream remote, `git fetch {upstream} --prune --prune-tags --tags`, and
base its work on the remote-tracking ref rather than a local branch.

Reuse of an existing worktree is the common way this goes wrong. Prefer creating a new one per
release. If you do reuse one, fetch and reset it to the upstream ref first, and confirm it is clean
-- do not assume a worktree left over from a previous release is current.

The child's kickoff prompt must carry everything it needs, because it does not share your context:

1. The instruction to run the **prepare-release** skill, **starting at Step 0**.
2. The source/base branch, already selected.
3. The target commit or ref, if the user chose one.
4. Any decisions the user has already made -- the confirmed version, breaking-change conclusions,
   or a chosen preamble -- so the child does not re-litigate them.
5. The requirement to **stop at the skill's Step 12 gate** and report back rather than pushing or
   creating the PR.
6. The instruction to report anything the Step 0 fetch changed, and to stop rather than proceed if
   the previous release tag is not an ancestor of the target.
7. The requirement to **stop at the skill's Step 10b gate** and bring the categorization table and
   acknowledgements roster back to you, so the user reviews notes content before a PR exists.

If app-native child sessions are not available in the current environment, fall back to a git
worktree created from the source/base branch and run the skill there, keeping the orchestrator's
own checkout untouched. The invariant is the worktree, not the mechanism.

## Recording the child

The moment you dispatch a child, write its identity into `release_session` -- `child_session_id`,
`child_worktree_path`, and `child_branch`. A release routinely outlives the session that started
it, and a worktree with no recorded owner is very hard to tell apart from the dozens of unrelated
worktrees a busy repository accumulates.

## Recovering an interrupted preparation

A child can stop anywhere: it fails, the user closes it, or the orchestrator session ends while the
child is mid-flight. Recovery starts from what the worktree actually contains, never from the fact
that it exists.

**Existence is not progress.** A `release-{version}` worktree proves only that a preparation was
started. Read its state before deciding anything:

| Evidence in the child's worktree | Where the preparation stopped |
|---|---|
| No `release-{version}` branch | Before Step 6; nothing to salvage |
| Branch exists, working tree dirty, no commit | Mid-preparation, somewhere in Steps 6-11 |
| Branch has a commit, nothing pushed | At the Step 12 gate, prepared and awaiting approval |
| Branch pushed, no PR | Interrupted inside Step 13 |
| PR open | Step 13 finished; this is stage 2, not stage 1 |

Then apply three rules:

- **Never reset or recreate a branch that has a commit on it.** It may hold work the user already
  reviewed and corrected -- release-note categorization, acknowledgement edits, a chosen preamble --
  none of which is reproducible from the repository. Read the commit and the drafted notes and
  continue from there.
- **Never inherit a validation result.** Build, pack, and ApiCompat outcomes leave no trace in git.
  A commit proves the files were written, not that anything passed. Re-run the checks rather than
  assuming the interrupted run got that far.
- **Prefer resuming the recorded child over launching a replacement.** It still holds the context.
  If it is gone, dispatch a replacement pointed at the *existing* worktree and branch, and tell it
  to audit what is already there before continuing -- not to start over.

Report the stopping point and the evidence you read, and let the user confirm before continuing.

Decisions the user made at a gate are the hardest thing to recover, because session tracking does
not survive the session. Their durable form is the artifact itself: the drafted release notes carry
the categorization, and the acknowledgements roster carries the exclusions. On resume, re-derive the
decisions by reading the drafted notes, and present them as *previously decided* for confirmation.
Silently re-deriving them from scratch will quietly undo corrections the user already made once.

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
