---
description: "SDK Tier Audit"

permissions:
  contents: read
  issues: read

safe-outputs:
  create-issue:
    title-prefix: "[C# SDK Tier Audit] "
    labels: [automation]
    close-older-issues: true

if: github.repository_owner == 'modelcontextprotocol' || github.event_name == 'workflow_dispatch'

concurrency:
  group: tier-audit-${{ github.event.inputs.scope || 'Conformance + Repo Health' }}
  cancel-in-progress: true

runtimes:
  node:
    version: "22"
  dotnet:
    version: "10.0"

network:
  allowed:
    - defaults
    - node
    - dotnet
    - github

timeout-minutes: 120

steps:
  - name: Write tier-check scorecard to file
    env:
      TIER_CHECK_JSON: ${{ needs.tier-check.outputs.tier_check_json }}
    run: echo "$TIER_CHECK_JSON" > /tmp/tier-check-scorecard.json

post-steps:
  - name: Upload audit report
    if: always()
    uses: actions/upload-artifact@v4
    with:
      name: audit-report
      path: /tmp/audit-report.md
      retention-days: 90
      if-no-files-found: ignore

on:
  schedule: weekly on thursday around 6:30am utc-5
  workflow_dispatch:
    inputs:
      scope:
        description: "Audit scope"
        required: true
        type: choice
        options:
          - Conformance + Repo Health
          - Repo Health
        default: "Conformance + Repo Health"
      output:
        description: "Where to publish results"
        required: true
        type: choice
        options:
          - Create Issue
          - Action Summary
        default: "Create Issue"
      csharp_sdk:
        description: "C# SDK (owner/repo:branch)"
        required: true
        type: string
        default: "modelcontextprotocol/csharp-sdk:main"
      conformance:
        description: "Conformance repo (owner/repo:branch)"
        required: true
        type: string
        default: "modelcontextprotocol/conformance:main"

  # ###############################################################
  # Override the COPILOT_GITHUB_TOKEN secret usage for the workflow
  # with a randomly-selected token from a pool of secrets.
  #
  # As soon as organization-level billing is offered for Agentic
  # Workflows, this stop-gap approach will be removed.
  #
  # See: /.github/actions/select-copilot-pat/README.md
  # ###############################################################

  # Add the pre-activation step of selecting a random PAT from the supplied secrets
  steps:
    - uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6.0.2
      name: Checkout the select-copilot-pat action folder
      with:
        persist-credentials: false
        sparse-checkout: .github/actions/select-copilot-pat
        sparse-checkout-cone-mode: true
        fetch-depth: 1

    - id: select-copilot-pat
      name: Select Copilot token from pool
      uses: ./.github/actions/select-copilot-pat
      env:
        SECRET_0: ${{ secrets.AUDIT_PAT_0 }}
        SECRET_1: ${{ secrets.AUDIT_PAT_1 }}
        SECRET_2: ${{ secrets.AUDIT_PAT_2 }}
        SECRET_3: ${{ secrets.AUDIT_PAT_3 }}
        SECRET_4: ${{ secrets.AUDIT_PAT_4 }}
        SECRET_5: ${{ secrets.AUDIT_PAT_5 }}
        SECRET_6: ${{ secrets.AUDIT_PAT_6 }}
        SECRET_7: ${{ secrets.AUDIT_PAT_7 }}
        SECRET_8: ${{ secrets.AUDIT_PAT_8 }}
        SECRET_9: ${{ secrets.AUDIT_PAT_9 }}

jobs:
  tier-check:
    runs-on: ubuntu-latest
    timeout-minutes: 30
    outputs:
      tier_check_json: ${{ steps.tier-check.outputs.result || steps.tier-check-health.outputs.result }}
      scope: ${{ steps.params.outputs.scope }}
      output: ${{ steps.params.outputs.output }}
      csharp_sdk_repo: ${{ steps.params.outputs.csharp_sdk_repo }}
      csharp_sdk_branch: ${{ steps.params.outputs.csharp_sdk_branch }}
      conformance_repo: ${{ steps.params.outputs.conformance_repo }}
      conformance_branch: ${{ steps.params.outputs.conformance_branch }}
    steps:
      - name: Resolve parameters
        id: params
        env:
          INPUT_SCOPE: ${{ github.event.inputs.scope || 'Conformance + Repo Health' }}
          INPUT_OUTPUT: ${{ github.event.inputs.output || 'Create Issue' }}
          INPUT_SDK: ${{ github.event.inputs.csharp_sdk || 'modelcontextprotocol/csharp-sdk:main' }}
          INPUT_CONF: ${{ github.event.inputs.conformance || 'modelcontextprotocol/conformance:main' }}
        run: |
          echo "scope=${INPUT_SCOPE}" >> "$GITHUB_OUTPUT"
          echo "output=${INPUT_OUTPUT}" >> "$GITHUB_OUTPUT"
          SDK_REPO="${INPUT_SDK%%:*}"
          SDK_BRANCH="${INPUT_SDK#*:}"
          echo "csharp_sdk_repo=${SDK_REPO}" >> "$GITHUB_OUTPUT"
          echo "csharp_sdk_branch=${SDK_BRANCH}" >> "$GITHUB_OUTPUT"
          CONF_REPO="${INPUT_CONF%%:*}"
          CONF_BRANCH="${INPUT_CONF#*:}"
          echo "conformance_repo=${CONF_REPO}" >> "$GITHUB_OUTPUT"
          echo "conformance_branch=${CONF_BRANCH}" >> "$GITHUB_OUTPUT"

      - name: Setup .NET SDK
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: "10.0"

      - name: Setup Node.js
        uses: actions/setup-node@v6
        with:
          node-version: "22"

      - name: Clone C# SDK
        env:
          SDK_BRANCH: ${{ steps.params.outputs.csharp_sdk_branch }}
          SDK_REPO: ${{ steps.params.outputs.csharp_sdk_repo }}
        run: git clone --depth 1 -b "$SDK_BRANCH" "https://github.com/${SDK_REPO}.git" /tmp/csharp-sdk

      - name: Clone conformance repo
        env:
          CONF_BRANCH: ${{ steps.params.outputs.conformance_branch }}
          CONF_REPO: ${{ steps.params.outputs.conformance_repo }}
        run: git clone --depth 1 -b "$CONF_BRANCH" "https://github.com/${CONF_REPO}.git" /tmp/conformance

      - name: Build conformance CLI
        run: cd /tmp/conformance && npm ci && npm run build

      - name: Build C# SDK
        if: steps.params.outputs.scope == 'Conformance + Repo Health'
        run: cd /tmp/csharp-sdk && dotnet build --verbosity quiet

      - name: Start conformance server
        if: steps.params.outputs.scope == 'Conformance + Repo Health'
        run: |
          cd /tmp/csharp-sdk
          nohup dotnet run --no-build --project tests/ModelContextProtocol.ConformanceServer --framework net9.0 -- --urls http://localhost:3003 > /tmp/conformance-server.log 2>&1 &
          timeout 60 bash -c 'until curl -sf http://localhost:3003/health; do sleep 1; done'

      - name: Run tier-check (Conformance + Repo Health)
        id: tier-check
        if: steps.params.outputs.scope == 'Conformance + Repo Health'
        env:
          GITHUB_TOKEN: ${{ github.token }}
        run: |
          cd /tmp/conformance
          REPO=$(cd /tmp/csharp-sdk && git remote get-url origin | sed 's#.*github.com[:/]##; s#\.git$##')
          BRANCH=$(cd /tmp/csharp-sdk && git rev-parse --abbrev-ref HEAD)
          set +e
          RESULT=$(node dist/index.js tier-check \
            --repo "$REPO" \
            --branch "$BRANCH" \
            --conformance-server-url http://localhost:3003 \
            --client-cmd "dotnet run --no-build --project /tmp/csharp-sdk/tests/ModelContextProtocol.ConformanceClient --framework net9.0 -- \$MCP_CONFORMANCE_SCENARIO" \
            --output json 2>/tmp/tier-check-stderr.txt)
          EXIT_CODE=$?
          set -e
          cat /tmp/tier-check-stderr.txt >&2
          if [ $EXIT_CODE -ne 0 ]; then
            echo "::error::tier-check CLI failed with exit code $EXIT_CODE"
            exit $EXIT_CODE
          fi
          EOF_MARKER=$(uuidgen)
          echo "result<<${EOF_MARKER}" >> "$GITHUB_OUTPUT"
          echo "$RESULT" >> "$GITHUB_OUTPUT"
          echo "${EOF_MARKER}" >> "$GITHUB_OUTPUT"

      - name: Run tier-check (Repo Health)
        id: tier-check-health
        if: steps.params.outputs.scope == 'Repo Health'
        env:
          GITHUB_TOKEN: ${{ github.token }}
        run: |
          cd /tmp/conformance
          REPO=$(cd /tmp/csharp-sdk && git remote get-url origin | sed 's#.*github.com[:/]##; s#\.git$##')
          BRANCH=$(cd /tmp/csharp-sdk && git rev-parse --abbrev-ref HEAD)
          set +e
          RESULT=$(node dist/index.js tier-check \
            --repo "$REPO" \
            --branch "$BRANCH" \
            --skip-conformance \
            --output json 2>/tmp/tier-check-stderr.txt)
          EXIT_CODE=$?
          set -e
          cat /tmp/tier-check-stderr.txt >&2
          if [ $EXIT_CODE -ne 0 ]; then
            echo "::error::tier-check CLI failed with exit code $EXIT_CODE"
            exit $EXIT_CODE
          fi
          EOF_MARKER=$(uuidgen)
          echo "result<<${EOF_MARKER}" >> "$GITHUB_OUTPUT"
          echo "$RESULT" >> "$GITHUB_OUTPUT"
          echo "${EOF_MARKER}" >> "$GITHUB_OUTPUT"

  pre-activation:
    outputs:
      copilot_pat_number: ${{ steps.select-copilot-pat.outputs.copilot_pat_number }}

engine:
  id: copilot
  env:
    # We cannot use line breaks in this expression as it leads to a syntax error in the compiled workflow
    # If none of the `AUDIT_PAT_#` secrets were selected, then the default COPILOT_GITHUB_TOKEN is used
    COPILOT_GITHUB_TOKEN: ${{ case(needs.pre_activation.outputs.copilot_pat_number == '0', secrets.AUDIT_PAT_0, needs.pre_activation.outputs.copilot_pat_number == '1', secrets.AUDIT_PAT_1, needs.pre_activation.outputs.copilot_pat_number == '2', secrets.AUDIT_PAT_2, needs.pre_activation.outputs.copilot_pat_number == '3', secrets.AUDIT_PAT_3, needs.pre_activation.outputs.copilot_pat_number == '4', secrets.AUDIT_PAT_4, needs.pre_activation.outputs.copilot_pat_number == '5', secrets.AUDIT_PAT_5, needs.pre_activation.outputs.copilot_pat_number == '6', secrets.AUDIT_PAT_6, needs.pre_activation.outputs.copilot_pat_number == '7', secrets.AUDIT_PAT_7, needs.pre_activation.outputs.copilot_pat_number == '8', secrets.AUDIT_PAT_8, needs.pre_activation.outputs.copilot_pat_number == '9', secrets.AUDIT_PAT_9, secrets.COPILOT_GITHUB_TOKEN) }}
---

# SDK Tier Audit

Perform a tier audit of the C# MCP SDK. The deterministic tier-check scorecard has already been computed by the `tier-check` job. Your job is to perform the AI-assisted evaluation (documentation coverage, policy review), apply tier logic, generate the report, and publish the results.

## Inputs

- **Scope**: ${{ needs.tier-check.outputs.scope }}
- **Output mode**: ${{ needs.tier-check.outputs.output }}
- **C# SDK**: ${{ needs.tier-check.outputs.csharp_sdk_repo }} (branch: ${{ needs.tier-check.outputs.csharp_sdk_branch }})
- **Conformance**: ${{ needs.tier-check.outputs.conformance_repo }} (branch: ${{ needs.tier-check.outputs.conformance_branch }})

The scorecard is at `/tmp/tier-check-scorecard.json` (written by a pre-agent step from the `tier-check` job output).

## Tier-Check Scorecard (pre-computed)

The deterministic tier-check scorecard has been pre-computed by the `tier-check` job and written to `/tmp/tier-check-scorecard.json`. Read this file to get the full JSON scorecard. **Do not re-run the tier-check CLI.**

```bash
cat /tmp/tier-check-scorecard.json
```

## Step 1: Setup

Clone both repositories for the AI-assisted evaluations. Use shallow clones.

```bash
git clone --depth 1 -b <csharp_sdk_branch> https://github.com/<csharp_sdk_repo>.git /tmp/csharp-sdk
git clone --depth 1 -b <conformance_branch> https://github.com/<conformance_repo>.git /tmp/conformance
```

You do NOT need to build either repo or start any servers — the conformance tests have already run.

## Step 2: AI-Assisted Evaluation

Read the **"Any Other AI Coding Agent"** section from `/tmp/conformance/.claude/skills/mcp-sdk-tier-audit/README.md`. Follow steps **2 through 5** only (skip step 1 — the CLI has already run):

2. **Evaluate documentation coverage** using the prompt in `references/docs-coverage-prompt.md`
3. **Evaluate policies** using the prompt in `references/policy-evaluation-prompt.md` — pass the `policy_signals` section from the tier-check JSON above
4. **Apply tier logic** using the thresholds in `references/tier-requirements.md` — combine the scorecard above with your evaluation results
5. **Generate report** using the template in `references/report-template.md`

The SDK checkout at `/tmp/csharp-sdk` is the local path for documentation and policy evaluations.

Write the assessment and remediation reports to `/tmp/conformance/results/`:
- `results/<YYYY-MM-DD>-csharp-sdk-assessment.md`
- `results/<YYYY-MM-DD>-csharp-sdk-remediation.md`

## Step 3: Compose the Audit Report

After the evaluation completes, compose the full audit report as a single markdown file at `/tmp/audit-report.md`. This file is used for both issue creation and the action summary artifact.

### Report structure

The report must contain these sections in order:

1. **Executive summary** — Use bullet points (not a paragraph). Include:
   - The tier classification result (e.g., "**Tier 1** — all requirements met")
   - Key conformance results (server and client pass rates)
   - Repo health highlights (triage compliance, P0 status, stable release version)
   - Documentation coverage (N/48 features)
   - Any blockers for the next tier up

2. **Full assessment** — Include the complete contents of `results/<YYYY-MM-DD>-csharp-sdk-assessment.md` verbatim. Do not modify, summarize, or reformat.

3. **Remediation guide** — Include the complete contents of `results/<YYYY-MM-DD>-csharp-sdk-remediation.md` verbatim. Do not modify, summarize, or reformat.

Use horizontal rules (`---`) to separate sections.

### Write the report

Write the composed report to `/tmp/audit-report.md`. This file will be uploaded as a workflow artifact by a post-execution step.

### Write the action summary

Always write the full audit report to the GitHub Step Summary so it appears on the workflow run's summary page:

```bash
cat /tmp/audit-report.md >> "$GITHUB_STEP_SUMMARY"
```

## Step 4: Publish Results

### If output mode is "Create Issue"

Create a GitHub issue using the `create-issue` safe output.

**Issue title** — The dynamic part (after the `[C# SDK Tier Audit] ` prefix):

- **"Conformance + Repo Health" scope**: `<YYYY-MM-DD> - Tier <N>`
- **"Repo Health" scope**: `<YYYY-MM-DD> - Tier <N> (Repo Health)`

Where `<YYYY-MM-DD>` is today's date and `<N>` is the computed tier number (1, 2, or 3).

**Issue body** — Use the contents of `/tmp/audit-report.md` as the issue body.

### If output mode is "Action Summary"

Do NOT create an issue. The report is already written to `$GITHUB_STEP_SUMMARY` and will be uploaded as an artifact. No further action is needed.

## Failure Handling

If the evaluation fails at any step, or if the audit does not produce assessment/remediation results:

1. **Do NOT create an issue.** Do not use the `create-issue` safe output.
2. **Write a GitHub Step Summary** explaining what happened:

```bash
echo "## Tier Audit: No Results" >> "$GITHUB_STEP_SUMMARY"
echo "" >> "$GITHUB_STEP_SUMMARY"
echo "The tier audit did not produce results. Reason: <describe the failure>" >> "$GITHUB_STEP_SUMMARY"
echo "" >> "$GITHUB_STEP_SUMMARY"
echo "No issue was filed for this run." >> "$GITHUB_STEP_SUMMARY"
```

3. The `noop` safe output will apply automatically when no `create-issue` output is produced.

---

## PAT Pool Setup

> **Note:** This section is for **repository maintainers** setting up PAT pool secrets. It is not consumed by the agent.

This workflow uses its own PAT pool (`AUDIT_PAT_0` through `AUDIT_PAT_9`) separate from the standard `COPILOT_PAT` pool. This allows audit-specific PATs with the minimum required scopes.

### Required PAT configuration

| Setting | Value |
|---------|-------|
| **Resource owner** | Your **user account** (not an organization) |
| **Repository access** | **Public Repositories (read-only)** — or, if either the C# SDK or conformance repos are private, add those repos explicitly |
| **Copilot Requests** | **Read** |

The PAT needs **Copilot Requests (Read)** for the Copilot engine. It also needs read access to the C# SDK and conformance repositories used by this workflow (configured via the `csharp_sdk_repo` and `conformance_repo` inputs, defaulting to `modelcontextprotocol/csharp-sdk` and `modelcontextprotocol/conformance`). If those repositories are public, selecting **Public Repositories (read-only)** is sufficient. If either repository is private, add it explicitly under **Repository access** in the PAT configuration.

### Create a PAT

[Use this link to prefill the PAT creation form][create-pat] with the required settings:

1. Set **Resource owner** to your **user account** (not an organization).
2. Ensure **Copilot Requests (Read)** is the only permission granted.
3. Set **Repository access** to **Public Repositories (read-only)** — or add the C# SDK and conformance repos explicitly if they are not public.
4. The **Token Name** does not need to match the secret name — use something recognizable like `MCP C# SDK: AUDIT_PAT_0`.

### Add the PAT as a repository secret

Add your PAT as a repository secret named `AUDIT_PAT_0` through `AUDIT_PAT_9` in the repository's **Settings > Secrets and variables > Actions** page:

```bash
gh secret set "AUDIT_PAT_0" --body "<your-pat>" --repo <owner/repo>
```

### Renewal

Set a recurring reminder to regenerate and update your PAT before it expires. PATs can be renewed on the same day each cycle.

[create-pat]: https://github.com/settings/personal-access-tokens/new?name=MCP+C%23+SDK%3A+AUDIT_PAT_%23&description=PAT+for+the+MCP+C%23+SDK+tier+audit+agentic+workflow.+Must+be+configured+with+Copilot+Requests+(Read)+permission+and+read+access+to+the+conformance+and+csharp-sdk+repos+(Public+Repositories+if+both+are+public).+User+account+as+resource+owner.&user_copilot_requests=read
