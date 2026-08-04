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
    stage       INTEGER PRIMARY KEY,   -- 1..5
    name        TEXT NOT NULL,
    status      TEXT NOT NULL,         -- 'pending' | 'in_progress' | 'blocked' | 'done' | 'carried_over'
    started_at  TEXT,                  -- ISO-8601 local time
    ended_at    TEXT,
    notes       TEXT
);

CREATE TABLE IF NOT EXISTS release_interactions (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    stage       INTEGER NOT NULL,
    kind        TEXT NOT NULL,         -- 'gate' | 'question' | 'review' | 'decision'
    prompted_at TEXT NOT NULL,         -- when you asked
    answered_at TEXT,                  -- when the user's answer arrived
    summary     TEXT
);
```

Seed the five stages up front:

```sql
INSERT OR IGNORE INTO release_stages (stage, name, status) VALUES
  (1, 'Prepare',          'pending'),
  (2, 'Review and merge', 'pending'),
  (3, 'Publish',          'pending'),
  (4, 'Release',          'pending'),
  (5, 'Verify',           'pending');
```

## Recording timestamps

Every timestamp comes from the current date/time available to you in the session. Use ISO-8601 local
time, for example `2026-08-04T13:22:05-07:00`. Never estimate a timestamp you could have recorded.

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

## Recording interactions

Insert a `release_interactions` row every time you put a gate, question, or review in front of the
user: write `prompted_at` when you ask, and fill `answered_at` from the timestamp of their reply.

The interval between `prompted_at` and `answered_at` is the user's **think-and-respond time**. Sum
those intervals to estimate **active user-interaction time**. Everything else in the session's
wall-clock span is wait time while you, a build, CI, or a workflow was working.

Apply judgement when summing:

- Discard or cap any single interval that clearly represents the user stepping away rather than
  engaging -- an overnight gap between a gate and its answer is wait time, not interaction time.
  Note in the summary that such a gap was excluded.
- Long stretches where the user reviews a diff, release notes, or a PR **are** interaction time even
  though you were idle.
- Always label the result as an estimate.

## Progress rail

Render the rail from `release_stages` at every gating prompt:

```
[✓] 1 Prepare  →  [●] 2 Review and merge  →  [ ] 3 Publish  →  [ ] 4 Release  →  [ ] 5 Verify
```

Use `✓` for done, `●` for in progress, `⚠` for blocked, `↩` for carried over, and a blank for
pending. Add the current sub-step after the rail when one is active, for example
`current: waiting on CI (2 checks running)`.
