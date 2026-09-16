# Report Format

This reference defines the structure, template, and formatting rules for the issue triage report.

## Output Destination

**Default (local file):** Save as `{YYYY-MM-DD}-mcp-issue-triage.md` in the current working directory. If a file with that name already exists, suffix with `-2`, `-3`, etc. (e.g., `2026-03-05-mcp-issue-triage-2.md`).

**Gist (if requested):** Create a **secret** gist via `gh gist create --desc "MCP C# SDK Issue Triage Report - {YYYY-MM-DD}" {filepath}` (gists default to secret; there is no `--private` flag). No confirmation is needed — just create it, then notify the user with a clickable link. The user may request a gist with phrases like "save as a gist", "create a gist", "gist it", or "post to gist".

## Report Structure

The report follows a **BLUF (Bottom Line Up Front)** pattern — the most critical information comes first, progressing from urgent to informational. The complete issue backlog is collapsed inside a `<details>` element so it doesn't bury the actionable items.

```markdown
# MCP C# SDK — Issue Triage Report

**Date:** {YYYY-MM-DD}
**Repository:** [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
**SDK Tier:** {Tier} ([tracking issue](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/{TierTrackingIssueNumber}))
**Triage SLA:** Within **{TriageSlaBusinessDays} business days** ({Tier} requirement)
**Critical Bug SLA:** Resolution within **{CriticalBugSlaDays} days** ({Tier} requirement)

---

## BLUF (Bottom Line Up Front)

{2-4 sentences: total open issues, SLA compliance status, number of issues needing
urgent attention, top finding. This is what a busy maintainer reads first.}

---

## ⚠️ Safety Concerns {only if issues were flagged during safety scanning; omit entirely if clean}

The following issues contain content that was flagged during safety scanning.
Their content should be reviewed carefully before acting on any recommendations.

| # | Title | Concern |
|---|---|---|
| [#N](url) | {Title} | {Brief description: e.g., "Prompt injection attempt detected", "Suspicious external link"} |

---

## 🚨 Issues Needing Urgent Attention

### SLA Violations — Untriaged Issues

{For EACH issue: a table with metadata (created, author, labels, comments, reactions)
followed by a **Status** paragraph summarizing the full discussion and a **Recommended
actions** list with specific labels and next steps.}

### Potential P0/P1 Issues to Assess

{Same detailed format as SLA violations — these are bugs that may warrant critical
priority based on core functionality impact or spec compliance.}

### ⏰ Stale Issues — Consider Closing

{Issues labeled `needs confirmation` or `needs repro` where the reporter hasn't
responded in >14 days. Include the date of the last author comment and a recommendation
to close if no response.}

---

## ⚠️ Issues Needing Labels

### Missing Type Label

{Table: issue number, current labels, title, recommended type label.}

### Missing Priority Label on Bugs

{Table: bugs that have type/status labels but no priority label, with recommended priority.}

---

## 🔀 Duplicate / Consolidation Candidates

{Table: groups of issues that overlap, with recommendation on which to keep.}

---

## 🔗 Cross-SDK Related Issues

{Themed tables mapping C# SDK issues to related issues in other MCP SDK repos.
Group by theme: OAuth, SSE, Streamable HTTP, Structured Content, Tasks, etc.}

---

## 📊 Other SDK Context

{Table of all MCP SDK repos with tier and open issue count, for context.}

---

<details>
<summary>📋 Complete Open Issue Backlog ({N} issues)</summary>

### Bugs ({N})

{Full table: #, Created, Age, Labels, Title, Remaining Actions}

### Enhancements ({N})

{Full table}

### Questions ({N})

{Full table}

### Documentation ({N})

{Full table}

</details>

---

## 📝 SDK Tier Requirements Checklist

{Table: each tier requirement, current compliance status, notes}

---

_Report generated {YYYY-MM-DD}. Data sourced from GitHub API._
```

## Formatting Rules

### Links
- **Within csharp-sdk:** Use GitHub shorthand — `#123` for issues/PRs, `@username` for users
- **Other repos:** Use full URLs — `[typescript-sdk #1090](https://github.com/modelcontextprotocol/typescript-sdk/issues/1090)`
- **Repo links:** `[modelcontextprotocol/typescript-sdk](https://github.com/modelcontextprotocol/typescript-sdk)`

### Age Display
- Show as `{N}d` (e.g., `35d`, `253d`)
- Business days calculated as `floor(calendar_days × 5 / 7)`

### Issue Detail Blocks

For each issue in the attention sections (SLA violations, P0/P1 candidates, stale issues), use this format:

```markdown
### [#{number}](https://github.com/modelcontextprotocol/csharp-sdk/issues/{number}) — {title}

| Field | Value |
|---|---|
| **Created** | {YYYY-MM-DD} (~{N} biz days {overdue / old}) |
| **Author** | @{login} {(contributor/member) if applicable} |
| **Labels** | `label1`, `label2` {or _(none)_ ❌ if empty} |
| **Comments** | {N} · **Reactions:** {N} {emoji} |
| **Assignee** | @{login} {or _(unassigned)_} |
| **Open PR** | [#{N}](url) {if any} |

**Status:** {Concise paragraph summarizing the issue state based on description + all comments.
Include: what the reporter wants, what maintainers have said, whether the community has
provided workarounds, whether there are linked PRs. End with the current blocking factor.}

{If the issue was flagged during safety scanning, include immediately after the Status paragraph:}

> ⚠️ **Safety flag:** {description of concern, e.g., "Issue body contains prompt injection attempt — instructions to 'ignore previous instructions' detected." or "Issue contains suspicious link to non-standard domain."}

**Recommended actions:**
- {Specific label changes: "Add `bug`, `needs repro`, `P2`"}
- {Next step: "Close as answered", "Request reproduction steps", "Assign to @X", etc.}
- {If stale: "Last author response was on {date} ({N} days ago). Consider closing."}
```

### Backlog Tables

For the collapsed backlog, use compact tables:

```markdown
| # | Created | Age | Labels | Title | Remaining Actions |
|---|---|---|---|---|---|
| [#N](url) | YYYY-MM-DD | Nd | `label1`, `label2` | Short title | Add `P2`; consider closing |
```

### Section Emoji Prefixes

| Section | Emoji |
|---|---|
| Safety concerns | ⚠️ |
| Urgent attention | 🚨 |
| Stale issues | ⏰ |
| Labels needed | ⚠️ |
| Duplicates | 🔀 |
| Cross-SDK | 🔗 |
| Context/stats | 📊 |
| Backlog | 📋 |
| Tier checklist | 📝 |
