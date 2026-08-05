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

| Stage | Status | Elapsed | Interactions |
|---|---|---|---|
| 1. Prepare | ✓ | {h m} | ~{h m} |
| 2. Review and merge | ✓ | {h m} | ~{h m} |
| 2. Review and merge (attempt 2) | ✓ | {h m} | ~{h m} |
| 3. Publish | ✓ | {h m} | ~{h m} |
| 4. Release | ✓ | {h m} | ~{h m} |
| 5. Verify | ✓ | {h m} | ~{h m} |
| **Total session** | | **{h m}** | **~{h m}** |

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
3. **The Interactions column is per-stage active user time.** Sum that stage's
   `release_interactions` prompt-to-answer intervals and prefix with `~`: `~5m`, `~1h 3m`. Show
   `~0m` when the stage had no interactions at all, or when its measured intervals round to zero.
   Show `—` when the stage *did* have interactions but none of them carry a usable reply timestamp --
   that is missing data, not zero time, and must never be rendered as `~0m` or fabricated.
   Carried-over stages show `—` in both time columns.
4. **`~` is the only qualifier the table needs.** Never append "minimum", "at least", or a similar
   hedge to a cell -- the tilde already says the figure is estimated, and the table stays scannable.
   When stages show `—` or interactions went unmeasured, explain that in the narrative prose below
   rather than in the table.
5. **Active interaction time is always an estimate.** Label it with `~` and say it is estimated from
   prompt-to-answer intervals. In the narrative, report it as a floor with the unmeasured count
   beside it -- interactions whose reply timestamp was unavailable are counted, not silently dropped.
   Name any interval you excluded as a step-away gap.
6. **Reconcile the narrative with the table.** The `Active interaction` bullet must equal the table's
   total interaction cell. Where the two could differ -- excluded step-away gaps, `—` stages,
   zero-duration rows discarded as unmeasured -- name the discrepancy explicitly rather than letting
   the reader find it.
7. **Waiting time is measured, not inferred.** Sum the `release_waits` intervals. Never derive it by
   subtracting interaction time from the session total; that counts diagnosis and rework as waiting.
   Show whatever the two do not account for as `Unaccounted` rather than folding it into either.
   **If `Unaccounted` computes negative, wait and interaction rows overlapped** — the split is
   unsound, so omit the `Unaccounted` line, report the two measured totals, and state plainly that
   they overlap. Never publish a negative figure and never clamp it to zero, which would present a
   broken split as a clean one.
8. **Longest single wait comes from `release_waits` and `release_workflow_runs`**, not from the
   longest interaction. If waits were not recorded, say the data is unavailable instead of
   substituting the longest gate.
9. **Round to readable units.** `2h 14m`, `47m`, `3m`. Never show seconds.
10. **Omit rows and sections that do not apply.** No blocked stage means no note about one; no
    follow-ups means the section says `None.` rather than disappearing.
11. **Never speculate about time.** If the session lacks the data for a section, say so plainly
    instead of estimating.
