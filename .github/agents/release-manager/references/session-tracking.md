# Session Tracking

Track release stage progress and timing so the closing summary is accurate. Use the **SQL tool** for
storage. **Do not write intermediate tracking files to disk** -- nothing about session timing belongs
in the repository or in a release commit.

## Schema

Create these tables once, at the start of the session, before any stage work begins.

```sql
CREATE TABLE IF NOT EXISTS release_session (
    key   TEXT PRIMARY KEY,
    value TEXT
);
-- Expected keys: version, base_branch, release_branch, pr_number, draft_release_url,
-- published_release_url, session_started_at

CREATE TABLE IF NOT EXISTS release_stages (
    stage       INTEGER NOT NULL,      -- 1..5
    attempt     INTEGER NOT NULL DEFAULT 1,
    name        TEXT NOT NULL,
    status      TEXT NOT NULL,         -- 'pending' | 'in_progress' | 'blocked' | 'done' | 'carried_over'
    started_at  TEXT,                  -- ISO-8601 local time
    ended_at    TEXT,
    notes       TEXT,
    PRIMARY KEY (stage, attempt)
);

CREATE TABLE IF NOT EXISTS release_interactions (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    stage        INTEGER NOT NULL,
    kind         TEXT NOT NULL,        -- 'gate' | 'question' | 'review' | 'decision'
    prompted_at  TEXT NOT NULL,        -- when you asked
    answered_at  TEXT,                 -- when the user's answer arrived
    outcome      TEXT,                 -- what they decided
    summary      TEXT
);

-- Unattended time: child-agent work, CI watches, workflow watches, index polling.
-- Without this, wait time can only be inferred from stage wall-clock, which
-- silently folds in discussion and rework.
CREATE TABLE IF NOT EXISTS release_waits (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    stage       INTEGER NOT NULL,
    kind        TEXT NOT NULL,         -- 'child_work' | 'ci' | 'workflow' | 'index' | 'other'
    reason      TEXT NOT NULL,
    started_at  TEXT NOT NULL,
    ended_at    TEXT,
    ref         TEXT                   -- run id, PR number, package, etc.
);

CREATE TABLE IF NOT EXISTS release_workflow_runs (
    run_id       TEXT PRIMARY KEY,
    stage        INTEGER NOT NULL,
    name         TEXT NOT NULL,
    head_sha     TEXT,
    started_at   TEXT,
    ended_at     TEXT,
    conclusion   TEXT
);
```

Seed the five stages up front:

```sql
INSERT OR IGNORE INTO release_stages (stage, attempt, name, status) VALUES
  (1, 1, 'Prepare',          'pending'),
  (2, 1, 'Review and merge', 'pending'),
  (3, 1, 'Publish',          'pending'),
  (4, 1, 'Release',          'pending'),
  (5, 1, 'Verify',           'pending');
```

## Recording timestamps

Every timestamp comes from the current date/time available to you in the session. Use ISO-8601 local
time, for example `2026-08-04T13:22:05-07:00`. Never estimate a timestamp you could have recorded.

**Prefer immutable external evidence over your own observation.** You notice things late; the event
itself has a real timestamp. Query it and use it:

| Event | Authoritative source |
|---|---|
| PR merged | `gh pr view {n} --json mergedAt` |
| Release published | `gh release view v{version} --json publishedAt` |
| Workflow run start/end | `gh run view {id} --json startedAt,updatedAt,conclusion` |
| Commit created | the commit's author date |

Recording stage 4's end from the moment you noticed publication, rather than from `publishedAt`,
inflates that stage by your entire polling interval. The same applies to a merge you detect on a
later poll, and to workflow runs you attach to after they started.

- **Session start** -- write `session_started_at` into `release_session` at initialization.
- **Stage start** -- set `status = 'in_progress'` and `started_at` the moment you begin the stage's
  first substantive action (invoking the skill, or beginning a status assessment for a human-gate
  stage).
- **Stage end** -- set `status = 'done'` and `ended_at` the moment the stage's gate is satisfied
  (PR opened, PR merged, draft created, release published, packages listed) -- **not** when the user
  next speaks.
- **Blocked** -- set `status = 'blocked'` with a note when a stage cannot advance (red CI, an
  unresolved breaking-change decision, a failed release workflow). Leave `started_at` intact; the
  blocked span still counts toward that stage's wall-clock time.
- **Carried over** -- when resuming a release and evidence shows a stage completed in an earlier
  session, record it as `status = 'carried_over'` with `started_at` and `ended_at` left NULL. Never
  invent durations for work you did not observe.
- **Rework** -- when a stage that reached its gate has to be revisited (CI went red after the PR was
  opened, a corrective push, a re-run of a failed workflow), close the current attempt and insert a
  new row with `attempt + 1` rather than reopening the old one or stretching its `ended_at`. One
  aggregate row per stage hides the shape of the time: a stage 2 that reads as "2h 22m" tells you
  nothing about how much was CI, how much was review, and how much was remediation.

## Recording waits

Insert a `release_waits` row whenever you begin waiting on something that is not the user, and close
it when the wait ends. Cover child-session work, CI watches, workflow watches, and index polling.

This is what makes the closing summary's split honest. Without it, wait time can only be inferred by
subtracting interaction time from stage wall-clock, which quietly counts discussion, diagnosis, and
rework as waiting. With it, both halves are measured:

```
active interaction = sum of interaction intervals
waiting            = sum of wait intervals
unaccounted        = total - (active + waiting)
```

Report the unaccounted remainder rather than distributing it. A visible gap is information; a
silently absorbed one is a wrong number.

Record every CI and release workflow run in `release_workflow_runs` as you watch it, using the run's
own `startedAt` and `updatedAt`. This makes the longest-wait figure in the summary a lookup instead
of a recollection.

## Recording interactions

Insert a `release_interactions` row every time you put a gate, question, or review in front of the
user: write `prompted_at` when you ask, and fill `answered_at` from the timestamp of their reply.

**Close the open interaction before doing anything else with the user's reply.** At most one row per
stage should have a NULL `answered_at` at any moment -- the question you are currently waiting on.
When a reply arrives, your first action is to `UPDATE` that row with `answered_at` and `outcome`;
only then act on what they said. Deferring the update is how rows end up permanently NULL, because
by the time the work is done the arrival time is gone.

```sql
UPDATE release_interactions
SET answered_at = '{reply-timestamp}', outcome = '{what they decided}'
WHERE id = (SELECT MAX(id) FROM release_interactions WHERE answered_at IS NULL);
```

Two failure modes to avoid, both of which produce numbers that look fine and are wrong:

- **Never write `answered_at` equal to `prompted_at`.** A zero-duration interaction means the reply
  timestamp was unavailable, not that the user answered instantly. Leave it NULL and count the row
  as unmeasured.
- **Record choice-style prompts too.** A gate answered by picking an option is still interaction; if
  the mechanism gives you no reply timestamp, log the row with NULL `answered_at` so it appears in
  the unmeasured count rather than vanishing.

At wrap-up, report how many interactions were measured and how many were not. "~27m across six
gates, four more unmeasured" is an honest floor; "~27m" alone implies a precision that is not there.

The interval between `prompted_at` and `answered_at` is the user's **think-and-respond time**. Sum
those intervals to estimate **active user-interaction time**. Do not treat the rest of the session as
waiting by subtraction -- take waiting from `release_waits` and report the remainder as unaccounted.

Apply judgement when summing:

- Discard or cap any single interval that clearly represents the user stepping away rather than
  engaging -- an overnight gap between a gate and its answer is wait time, not interaction time.
  Note in the summary that such a gap was excluded.
- Long stretches where the user reviews a diff, release notes, or a PR **are** interaction time even
  though you were idle.
- Always label the result as an estimate, and state the measured/unmeasured split alongside it.

## Progress rail

Render the rail from the latest attempt of each stage in `release_stages` at every gating prompt:

```
[✓] 1 Prepare  →  [●] 2 Review and merge  →  [ ] 3 Publish  →  [ ] 4 Release  →  [ ] 5 Verify
```

Use `✓` for done, `●` for in progress, `⚠` for blocked, `↩` for carried over, and a blank for
pending. Add the current sub-step after the rail when one is active, for example
`current: waiting on CI (2 checks running)`.
