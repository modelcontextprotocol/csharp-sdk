# Release Wrap-Up Summary

Present this summary when the release is complete: the GitHub release is published, both the release
and docs workflows have succeeded, the packages are listed on NuGet.org, and the docs site reflects
the release.

The tone is short and celebratory. It is a chat message to the user -- **do not commit it, do not
post it to GitHub, and do not write it to a file** unless the user explicitly asks.

Build the timing sections from the `release_stages`, `release_interactions`, `release_waits`, and
`release_workflow_runs` tables described in [session-tracking.md](session-tracking.md).

## Template

```markdown
🎉 **v{version} is released.**

{One or two sentences on the release theme, echoing the preamble that shipped in the release notes.}

**Shipped**

| | |
|---|---|
| Version | `v{version}` |
| Base branch | `{base branch}` |
| Release PR | #{pr} |
| Release | {release URL} |
| Release workflow | {run URL} — {conclusion} |
| Docs workflow | {run URL} — {conclusion} |
| NuGet | {listed package versions, or the package listing URL} |
| Docs | https://csharp.sdk.modelcontextprotocol.io/{version-slug}/ — live |

**Packages**

* {package name} {version}
* {package name} {version}

**Stage timing (this session)**

| Stage | Status | Elapsed |
|---|---|---|
| 1. Prepare | ✓ | {h m} |
| 2. Review and merge | ✓ | {h m} |
| 2. Review and merge (attempt 2) | ✓ | {h m} |
| 3. Publish | ✓ | {h m} |
| 4. Release | ✓ | {h m} |
| 5. Verify | ✓ | {h m} |
| **Total session** | | **{h m}** |

{Include an attempt row only when a stage was reworked, and say what forced it -- "CI red, corrective
push". Aggregating rework into one row hides where the time actually went.}

**Where the time went**

* Active interaction — ~{h m} across {n} measured gates{, plus {n} unmeasured}
* Waiting on builds, CI, and the release and docs workflows — ~{h m}
* Unaccounted — {h m}
* Longest single wait — {h m} ({what you were waiting on})

{Optional: one line on anything notable — a blocked stage and how long it cost, an excluded
step-away gap, or a stage that ran unusually long or short.}

**Follow-ups**

* {Anything deferred during the release, or "None."}
* {Worktrees still on disk for this release, offered for cleanup, or omit this line.}
```

## Rules

1. **Only report what you observed.** Stages recorded as `carried_over` show `↩ carried over from a
   previous session` in the Status column and `—` for Elapsed. They are excluded from the total, and
   a footnote says the total covers this session only.
2. **Total session** is wall-clock from `session_started_at` to now, not the sum of stage elapsed
   times -- gaps between stages belong to the session but to no stage.
3. **Active interaction time is always an estimate.** Label it with `~` and say it is estimated from
   prompt-to-answer intervals. Report it as a floor with the unmeasured count beside it -- interactions
   whose reply timestamp was unavailable are counted, not silently dropped. Name any interval you
   excluded as a step-away gap.
4. **Waiting time is measured, not inferred.** Sum the `release_waits` intervals. Never derive it by
   subtracting interaction time from the session total; that counts diagnosis and rework as waiting.
   Show whatever the two do not account for as `Unaccounted` rather than folding it into either.
5. **Longest single wait comes from `release_waits` and `release_workflow_runs`**, not from the
   longest interaction. If waits were not recorded, say the data is unavailable instead of
   substituting the longest gate.
6. **Round to readable units.** `2h 14m`, `47m`, `3m`. Never show seconds.
7. **Omit rows and sections that do not apply.** No blocked stage means no note about one; no
   follow-ups means the section says `None.` rather than disappearing.
8. **Never speculate about time.** If the session lacks the data for a section, say so plainly
   instead of estimating.
