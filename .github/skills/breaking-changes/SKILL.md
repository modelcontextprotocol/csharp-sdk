---
name: breaking-changes
description: Audit pull requests for breaking changes in the C# MCP SDK. Examines PR descriptions, review comments, and diffs to identify API and behavioral breaking changes, then reconciles labels with user confirmation. Use when asked to audit breaking changes, check for breaking changes, or review a set of PRs for breaking impact.
compatibility: Requires gh CLI with repo access and GitHub API access for PR details, review history, and labels.
---

# Breaking Change Audit

Audit pull requests in the `modelcontextprotocol/csharp-sdk` repository for breaking changes. This skill examines a range of commits, identifies API and behavioral breaking changes, assesses their impact, reconciles `breaking-change` labels, and returns structured results.

## Input

The user provides a commit range in any of these forms:
- `tag..HEAD` (e.g. `v0.8.0-preview.1..HEAD`)
- `tag..tag` (e.g. `v0.8.0-preview.1..v0.9.0-preview.1`)
- `sha..sha`
- `tag..sha` or `sha..HEAD`

If no range is provided, ask the user to specify one.

Use the GitHub API to get the full list of PRs merged within the specified range.

## Process

### Step 1: Examine Every PR

For each PR in the range, study:
- PR description and linked issues
- Full review and comment history
- Complete code diff

Look for both categories of breaking changes:
- **API (compile-time)** — changes to public type signatures, parameter types, return types, removed members, sealed types, new obsoletion attributes, etc.
- **Behavioral (runtime)** — new/changed exceptions, altered return values, changed defaults, modified event ordering, serialization changes, etc.

See [references/classification.md](references/classification.md) for the full classification guide, including SDK-specific versioning policies (pre-1.0 preview flexibility, experimental APIs, obsoletion lifecycle, and spec-driven changes) that influence how breaks are assessed.

### Step 2: Assess Impact

For each identified breaking change, assess:
- **Breadth** — how many consumers are likely affected (widely-used type vs. obscure API)
- **Severity** — compile-time break (immediate build failure) vs. behavioral (subtle runtime difference)
- **Migration** — straightforward fix vs. significant code changes required

This assessment informs how breaking changes are ordered when presented (most impactful first).

### Step 3: Reconcile Labels

Compare findings against existing `breaking-change` labels on PRs.

Present mismatches to the user interactively:

- **Unlabeled but appears breaking** → explain why the PR appears breaking, ask user to confirm. If confirmed: apply the `breaking-change` label and ask the user whether to comment on the PR explaining the addition.
- **Labeled but does not appear breaking** → explain why, ask user to confirm removal. If confirmed: remove the label and ask the user whether to comment on the PR explaining the removal.

### Step 4: Present Results

Present the final list of confirmed breaking changes, sorted from most impactful to least, with:
- PR number and title
- Classification (API or behavioral)
- Impact assessment summary
- 1-2 bullet description of what breaks and migration guidance

## Output

The audit produces a structured list of breaking changes that can be consumed by other skills (e.g. the release-notes skill) or presented directly to the user.

Each entry contains:
- PR number and description
- Impact ranking (most → least impactful)
- Detail bullets describing the break and migration path
