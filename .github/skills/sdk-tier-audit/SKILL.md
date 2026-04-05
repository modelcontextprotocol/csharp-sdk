---
name: sdk-tier-audit
description: >-
  Perform a tier audit of the C# MCP SDK against SEP-1730 (the SDK Tiering System).
  Clones the conformance and C# SDK repositories, builds and runs conformance tests,
  runs the tier-check CLI, evaluates documentation and policies, and produces
  assessment and remediation reports. Delegates all audit logic to the
  mcp-sdk-tier-audit skill in the conformance repository.
compatibility: Requires git, .NET SDK 10.0+, Node.js 20+, and npm. Needs read access to the C# SDK and conformance GitHub repositories.
argument-hint: '[scope] [--csharp-sdk-repo <owner/repo>] [--csharp-sdk-branch <branch>] [--conformance-repo <owner/repo>] [--conformance-branch <branch>]'
---

# SDK Tier Audit

Perform a tier audit of the C# MCP SDK against [SEP-1730](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/1730) (the SDK Tiering System). This skill delegates all audit logic to the `mcp-sdk-tier-audit` skill from the conformance repository — it handles setup, then follows the conformance skill's instructions.

If any step fails, stop and report the error to the user. Do not proceed to the next step.

## Arguments

Parse optional arguments from the user's input:

- **scope** — `Conformance + Repo Health` (default) or `Repo Health`
- **--csharp-sdk-repo** — C# SDK repository as `owner/repo` (default: `modelcontextprotocol/csharp-sdk`)
- **--csharp-sdk-branch** — C# SDK branch (default: `main`)
- **--conformance-repo** — Conformance repository as `owner/repo` (default: `modelcontextprotocol/conformance`)
- **--conformance-branch** — Conformance repo branch (default: `main`)

If the user provides just a scope keyword (e.g., `/sdk-tier-audit Repo Health`), use that as the scope. All other arguments use the defaults if not specified.

## Prerequisites

The following tools must be available:

- **git** — to clone repositories
- **.NET SDK** (10.0+) — to build and run the C# SDK conformance server/client
- **Node.js** (20+) and **npm** — to build and run the conformance CLI

## Step 1: Clone and Build

### Clone the C# SDK

**macOS / Linux:**

```bash
git clone --depth 1 -b <csharp_sdk_branch> https://github.com/<csharp_sdk_repo>.git /tmp/csharp-sdk
```

**Windows (PowerShell):**

```powershell
git clone --depth 1 -b <csharp_sdk_branch> https://github.com/<csharp_sdk_repo>.git $env:TEMP\csharp-sdk
```

### Clone the conformance repository

**macOS / Linux:**

```bash
git clone --depth 1 -b <conformance_branch> https://github.com/<conformance_repo>.git /tmp/conformance
```

**Windows (PowerShell):**

```powershell
git clone --depth 1 -b <conformance_branch> https://github.com/<conformance_repo>.git $env:TEMP\conformance
```

### Build the conformance CLI

**macOS / Linux:**

```bash
cd /tmp/conformance && npm ci && npm run build
```

**Windows (PowerShell):**

```powershell
cd $env:TEMP\conformance
npm ci
if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
npm run build
if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }
```

Use `/tmp` paths on macOS/Linux and `$env:TEMP` paths on Windows throughout the remaining steps.

## Step 2: Start Conformance Server (if scope includes conformance)

Skip this step if the scope is "Repo Health".

### Build the C# SDK

```bash
cd /tmp/csharp-sdk && dotnet build
```

### Start the conformance server

The server must remain running throughout the audit. Use `nohup` (macOS/Linux) or `Start-Process` (Windows) to prevent the process from dying when the shell session changes.

**macOS / Linux:**

```bash
cd /tmp/csharp-sdk
nohup dotnet run --no-build --project tests/ModelContextProtocol.ConformanceServer --framework net9.0 -- --urls http://localhost:3003 > /tmp/conformance-server.log 2>&1 &
# Wait for the server to be ready (macOS lacks `timeout`, so use a loop)
for i in $(seq 1 60); do
  curl -sf http://localhost:3003/health > /dev/null 2>&1 && break
  sleep 1
done
curl -sf http://localhost:3003/health > /dev/null || { echo "Server failed to start — check /tmp/conformance-server.log"; exit 1; }
echo "Conformance server ready"
```

**Windows (PowerShell):**

```powershell
cd $env:TEMP\csharp-sdk
Start-Process -NoNewWindow dotnet -ArgumentList "run","--project","tests/ModelContextProtocol.ConformanceServer","--framework","net9.0","--","--urls","http://localhost:3003"
# Wait for the server to be ready
$timeout = 60; $elapsed = 0
while ($elapsed -lt $timeout) {
    try { Invoke-WebRequest -Uri http://localhost:3003/health -UseBasicParsing -ErrorAction Stop | Out-Null; break }
    catch { Start-Sleep 1; $elapsed++ }
}
if ($elapsed -ge $timeout) { throw "Conformance server did not start within $timeout seconds" }
```

## Step 3: Run the Audit

Read the **"Any Other AI Coding Agent"** section from the conformance skill's README:

- macOS/Linux: `/tmp/conformance/.claude/skills/mcp-sdk-tier-audit/README.md`
- Windows: `$env:TEMP\conformance\.claude\skills\mcp-sdk-tier-audit\README.md`

Follow those instructions exactly, using the reference files in the `references/` directory alongside it.

The instructions describe a 5-step process:

1. **Run the tier-check CLI** to get the deterministic scorecard
2. **Evaluate documentation coverage** using the prompt in `references/docs-coverage-prompt.md`
3. **Evaluate policies** using the prompt in `references/policy-evaluation-prompt.md`
4. **Apply tier logic** using the thresholds in `references/tier-requirements.md`
5. **Generate report** using the template in `references/report-template.md`

### CLI parameters

Derive the `owner/repo` from the C# SDK clone's git remote:

```bash
cd /tmp/csharp-sdk && git remote get-url origin | sed 's#.*github.com[:/]##; s#\.git$##'
```

Derive the branch from the local checkout:

```bash
cd /tmp/csharp-sdk && git rev-parse --abbrev-ref HEAD
```

**If scope is "Conformance + Repo Health"**, run with both server and client conformance:

```bash
cd /tmp/conformance
node dist/index.js tier-check \
  --repo <owner/repo> \
  --branch <branch> \
  --conformance-server-url http://localhost:3003 \
  --client-cmd 'dotnet run --no-build --project /tmp/csharp-sdk/tests/ModelContextProtocol.ConformanceClient --framework net9.0 -- $MCP_CONFORMANCE_SCENARIO' \
  --output json
```

**If scope is "Repo Health"**, run without conformance:

```bash
cd /tmp/conformance
node dist/index.js tier-check \
  --repo <owner/repo> \
  --branch <branch> \
  --skip-conformance \
  --output json
```

### Documentation and policy evaluation

After running the CLI, perform the documentation coverage and policy evaluations by reading and following the prompts in the reference files. The SDK checkout at `/tmp/csharp-sdk` (or `$env:TEMP\csharp-sdk` on Windows) is the local path for these evaluations.

### Report generation

Write the assessment and remediation reports to the conformance repo's `results/` directory following the template in `references/report-template.md`:

- `results/<YYYY-MM-DD>-csharp-sdk-assessment.md`
- `results/<YYYY-MM-DD>-csharp-sdk-remediation.md`

## Step 4: Present Results

After the audit completes, present the user with:

1. **Executive summary** — The tier classification and primary reasons (3-5 sentences)
2. **Report file locations** — Paths to the assessment and remediation files
3. **Key gaps** — Top items needed for tier advancement

If the audit failed at any step, explain what happened and which step failed.
