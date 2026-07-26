---
name: run-conformance-from-branch
description: Run MCP conformance tests in the C# SDK against a conformance branch (including forks) instead of the published npm version, then restore pinned dependencies.
compatibility: Requires npm, node, and dotnet SDK. Uses the csharp-sdk repo package.json/package-lock.json and tests/ModelContextProtocol.AspNetCore.Tests.
---

# Run Conformance From Branch

Run C# SDK conformance tests against an unpublished `modelcontextprotocol/conformance` branch (including branches in forks).

## Use Cases

- Validate a conformance PR before it is published to npm
- Validate C# SDK behavior against a fork with custom scenario changes
- Reproduce failures caused by conformance changes

## Safety / Repo Hygiene

1. Start from a clean git state.
2. Commit or stash local changes first.
3. Restore pinned dependencies when done (`npm ci`).

## Inputs

- **Source type**: `upstream-branch` or `fork-branch`
- **Source locator**:
  - Upstream branch: `modelcontextprotocol/conformance#<branch>`
  - Fork branch: `<owner>/conformance#<branch>`
- **Scenario** (optional): e.g. `auth/scope-step-up`

## Workflows

### A) Install directly from GitHub branch (upstream or fork)

From `csharp-sdk` root:

```bash
npm install --no-save @modelcontextprotocol/conformance@github:<owner>/conformance#<branch>
```

Examples:

```bash
npm install --no-save @modelcontextprotocol/conformance@github:modelcontextprotocol/conformance#main
npm install --no-save @modelcontextprotocol/conformance@github:myuser/conformance#sep-2350-check
```

## Run Tests

### Run client conformance tests with dotnet test filter:

```bash
dotnet test tests/ModelContextProtocol.AspNetCore.Tests/ModelContextProtocol.AspNetCore.Tests.csproj -f net10.0 --filter "FullyQualifiedName~ClientConformanceTests"
```

### Run server conformance tests with dotnet test filter:

```bash
dotnet test tests/ModelContextProtocol.AspNetCore.Tests/ModelContextProtocol.AspNetCore.Tests.csproj -f net10.0 --filter "FullyQualifiedName~ServerConformanceTests"
```

## Reporting

Always report:

1. Installed conformance source (`npm ls @modelcontextprotocol/conformance --depth=0`)
2. Scenario results (pass/fail/warnings)
3. Any new check IDs observed (for traceability)

## Cleanup / Restore

Return repo to pinned dependency state:

```bash
npm ci
```
