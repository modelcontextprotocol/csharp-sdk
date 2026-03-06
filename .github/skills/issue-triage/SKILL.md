---
name: issue-triage
description: Generate an issue triage report for the C# MCP SDK. Fetches all open issues, evaluates SLA compliance against SDK tier requirements, reviews issue discussions for status and next steps, cross-references related issues in other MCP SDK repos, and produces a BLUF markdown report. Use when asked to triage issues, audit SLA compliance, review open issues, or generate an issue report.
compatibility: Requires GitHub API access for issues, comments, labels, and pull requests across modelcontextprotocol repositories. Requires gh CLI for optional gist creation.
---

# Issue Triage Report

Generate a comprehensive, prioritized issue triage report for the `modelcontextprotocol/csharp-sdk` repository. The C# SDK is **Tier 1** ([tracking issue](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/2261)), so apply the Tier 1 SLA thresholds (for triage, P0 resolution, and other applicable timelines) as defined in the live Tier 1 requirements fetched from `sdk-tiers.mdx` in Step 1. **Triage** means the issue has at least one type label (`bug`, `enhancement`, `question`, `documentation`) or status label (`needs confirmation`, `needs repro`, `ready for work`, `good first issue`, `help wanted`).

The report follows a **BLUF (Bottom Line Up Front)** structure — leading with the most critical findings and progressing to less-urgent items, with the full backlog collapsed to keep attention on what matters.

## Process

Work through each step sequentially. The skill is designed to run end-to-end without user intervention.

### Step 1: Fetch SDK Tier 1 SLA Criteria

Fetch the live `sdk-tiers.mdx` from:
```
https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/refs/heads/main/docs/community/sdk-tiers.mdx
```

Extract the Tier 1 requirements — triage SLA, critical bug SLA, label definitions (type, status, priority), and P0 criteria. These values drive all classification and SLA calculations in subsequent steps.

**If the fetch fails, stop and inform the user.** Do not proceed without live tier data.

### Step 2: Fetch All Open Issues

Paginate through all open issues in `modelcontextprotocol/csharp-sdk` via the GitHub API. For each issue, capture:
- Number, title, body (description)
- Author and author association (member, contributor, none)
- Created date, updated date
- All labels
- Comment count
- Assignees

### Step 3: Classify Triage Status

Using the label definitions extracted from `sdk-tiers.mdx` in Step 1, classify each issue:

| Classification | Criteria |
|---------------|---------|
| **Has type label** | Has one of the type labels defined in the tier document |
| **Has status label** | Has one of the status labels defined in the tier document |
| **Has priority label** | Has one of the priority labels defined in the tier document |
| **Is triaged** | Has at least one type OR status label |
| **Business days since creation** | `floor(calendar_days × 5 / 7)` (approximate, excluding weekends) |
| **SLA compliant** | Triaged within the tier's required window using the business-day calculation above |

Compute aggregate metrics:
- Total open issues
- Count triaged vs. untriaged
- Count of SLA violations
- Counts by type, status, and priority label
- Count missing each label category

### Step 4: Identify Issues Needing Attention

Build prioritized lists of issues that need action. These are the issues that will receive deep-dive review in Step 5.

**4a. SLA Violations** — Untriaged issues exceeding the tier's triage SLA threshold.

**4b. Missing Type Label** — Issues that have a status label but no type label. These are technically triaged but incompletely labeled.

**4c. Potential P0/P1 Candidates** — Bugs (or unlabeled issues that appear to be bugs) that may warrant P0 or P1 priority based on keywords or patterns:
- Core transport failures (SSE hanging, Streamable HTTP broken, connection drops)
- Spec non-compliance (protocol violations, incorrect OAuth handling)
- Security vulnerabilities
- NullReferenceException / crash reports
- Issues with high reaction counts or many comments

**4d. Stale `needs confirmation` / `needs repro`** — Issues labeled `needs confirmation` or `needs repro` where the last comment from the issue author (not a maintainer or bot) is more than 14 days ago. These are candidates for closing.

**4e. Duplicate / Consolidation Candidates** — Issues with substantially overlapping titles or descriptions. Group them and recommend which to keep and which to close.

### Step 5: Deep-Dive Review of Attention Items

For every issue identified in Step 4 (SLA violations, missing type, potential P0/P1, stale issues, duplicates), perform a thorough review:

1. **Read the full issue description** — understand the reporter's problem and what they're asking for.
2. **Read ALL comments** — understand the full discussion history, including:
   - Maintainer responses and their positions
   - Community workarounds or solutions
   - Whether the reporter confirmed a fix or workaround
   - Any linked PRs (open or merged)
3. **Summarize current status** — write a concise paragraph describing where the issue stands today.
4. **Recommend labels** — specify which type, status, and priority labels should be applied and why.
5. **Recommend next steps** — one of:
   - **Close**: if the issue is answered, resolved, or stale without response
   - **Label and keep**: if the issue is valid but needs triage labels
   - **Needs investigation**: if the issue is potentially serious but unconfirmed
   - **Link to PR**: if there's an open PR addressing it
   - **Consolidate**: if it duplicates another issue (specify which)
6. **Flag stale issues** — if `needs confirmation` or `needs repro` and the last comment from the reporter is >14 days ago, explicitly note: _"Last author response was on {date} ({N} days ago). Consider closing if no response is received."_

### Step 6: Cross-SDK Analysis

Using the repository list from [references/cross-sdk-repos.md](references/cross-sdk-repos.md):

1. Search each other MCP SDK repo for open issues with related themes. Use the search themes listed in the reference document.
2. For each C# SDK issue that has a related issue in another repo, note the cross-reference.
3. Group cross-references by theme (OAuth, SSE, Streamable HTTP, etc.) for the report.
4. Note the total open issue count for each SDK repo for context.

This step adds significant value but also significant API calls. If the user asks to skip cross-SDK analysis, respect that.

### Step 7: Generate Report

Produce the triage report following the template in [references/report-format.md](references/report-format.md). The report must follow the BLUF structure with urgency-descending ordering.

**Output destination:**
- **Default (local file):** Save as `{YYYY-MM-DD}-mcp-issue-triage.md` in the current working directory. If a file with that name already exists, suffix with `-2`, `-3`, etc.
- **Gist (if requested):** If the user asked to save as a gist, create a **secret** gist using `gh gist create` with a `--desc` describing the report. No confirmation is needed — create the gist, then notify the user with a clickable link to it.

The user may request a gist with phrases like "save as a gist", "create a gist", "gist it", "post to gist", etc.

### Step 8: Present Summary

After generating the report, display a brief console summary to the user:
- Total open issues and triage metrics (triaged/untriaged/SLA violations)
- Top 3-5 most urgent findings
- Where the full report was saved (file path or gist URL)

## Edge Cases

- **Issue has only area labels** (e.g., `area-auth`, `area-infrastructure`): these are NOT type or status labels. The issue is untriaged unless it also has a type or status label.
- **Closed-then-reopened issues**: treat as open; use the original creation date for SLA calculation.
- **Issues filed by maintainers/contributors**: still subject to triage SLA — all issues need labels regardless of author.
- **Issues that are tracking issues or meta-issues**: may legitimately lack status labels. Note them but don't flag as SLA violations if they have a type label.
- **Very old issues (>1 year)**: note age but don't treat all old issues as urgent — they may be intentionally kept open as long-term feature requests.
- **Rate limiting**: if GitHub API rate limits are hit during cross-SDK analysis, complete the analysis for repos already fetched and note which repos were skipped.
