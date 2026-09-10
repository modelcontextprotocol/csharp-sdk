# API Compatibility and Diff Guide

This reference describes how to run API compatibility checks and generate API diff reports for the C# MCP SDK release process.

## API Compatibility Check (ApiCompat)

The SDK uses NuGet's [Package Validation](https://learn.microsoft.com/dotnet/fundamentals/package-validation/overview) to verify API compatibility between releases. This is configured in `src/Directory.Build.props`:

```xml
<EnablePackageValidation>true</EnablePackageValidation>
<PackageValidationBaselineVersion>{baseline}</PackageValidationBaselineVersion>
```

Read the current values rather than assuming them, and check whether any individual project overrides them — a project that opts out of validation still ships, and needs to be reported as unvalidated rather than quietly skipped.

### Running ApiCompat

1. **Pack the SDK packages** to trigger validation. Enumerate the packable projects under `src/` and pack each one; the set of shipping packages grows over time, so do not work from a remembered list:
   ```sh
   dotnet pack src/{project}/{project}.csproj
   ```
   Or pack all at once:
   ```sh
   dotnet pack
   ```

2. **Capture the output.** Package validation compares the current public API against the baseline version downloaded from NuGet. Any compatibility issues appear as build warnings or errors.

3. **Interpret results:**
   - **No issues**: The API is backward-compatible with the baseline. This is the expected result for PATCH and MINOR releases.
   - **`Unnecessary suppressions found`**: **Read this before concluding anything else.** See [Reading a failing run](#reading-a-failing-run) below — the CP lines that follow it are usually *not* live breaks.
   - **Compatibility errors**: The API has breaking changes relative to the baseline. These should align with the breaking change audit from Step 3 of the prepare-release skill.
   - **Suppressions needed**: If intentional breaking changes are confirmed, add entries to `CompatibilitySuppressions.xml` in the affected project directory — but only after completing the [baseline-transition suppression audit](#baseline-transition-suppression-audit).

### Reading a failing run

`Unnecessary suppressions found` is itself a **hard failure**, not a warning attached to some other
problem. When it appears, the `CP0001` / `CP0002` / `CP0005` lines printed after it are the tool's
**detailed listing of the suppression entries it considers unused**. They are not a list of live API
breaks, even though they are formatted identically and appear under the same error banner.

Misreading that listing is how a routine release turns into a phantom emergency. In this repo it
produced 312 apparent breaking changes across the Core Tasks API on a release whose only real
change was one additive method — and it did so convincingly, because 312 lines of CP0001 for
missing types reads exactly like a catastrophic regression.

Before classifying a release as breaking, confirm which of the two you are looking at:

1. **Regenerate the suppression file** (see the audit below). If the generated output is *empty*,
   there are no live breaks and every tracked entry is stale.
2. **Cross-check the direct API diff.** If ApiDiff shows only the additions you expect, the CP lines
   are not describing reality.

Never work around this with `ApiCompatPermitUnnecessarySuppressions`, `NoWarn` for CP diagnostics,
or by disabling baseline validation. Those hide the signal that tells you the suppressions and the
baseline have drifted apart, which is the one thing you need to know.

### Updating the Baseline Version

- **MAJOR version bump**: Update `<PackageValidationBaselineVersion>` to the previous release version so that ApiCompat validates against the last stable release of the prior MAJOR version. After the new MAJOR release is published, the baseline stays at the new version for future comparisons.
- **MINOR or PATCH version bump**: Keep `<PackageValidationBaselineVersion>` at the last MAJOR release version (e.g., keep `1.0.0` when releasing `1.1.0` or `1.0.1`).

**A baseline that trails `VersionPrefix` is the expected steady state, not a stale value.** Through a MAJOR series the baseline deliberately stays put while `VersionPrefix` advances, so seeing `2.0.0` alongside a published `2.1.0` means the rule is being followed. Do not "fix" the gap — bumping the baseline mid-series triggers the audit below and invites the released API surface to be re-baselined against itself, silently discarding the compatibility guarantee the property exists to enforce.

**Any change to this property triggers the [baseline-transition suppression audit](#baseline-transition-suppression-audit).** Do not change it and interpret the resulting failures as breaking changes — the failures are expected until the suppressions are reconciled.

### Baseline-transition suppression audit

**Whenever `PackageValidationBaselineVersion` changes, run this audit before interpreting any
ApiCompat failure and before adding a single suppression entry.**

Suppression entries are scoped to the baseline they were generated against. They record "this
difference from *that* baseline is intentional." Move the baseline and the differences change, so
entries written for the old baseline may describe nothing at all — the API they excused is now
present on both sides. The tool reports those orphans as unnecessary, and the build fails.

For **every shipping project**:

1. Inventory the tracked suppressions:
   ```sh
   ls src/*/CompatibilitySuppressions.xml
   ```
2. Regenerate what the *current* baseline actually requires, into a throwaway file so the tracked
   one is not overwritten while you are still deciding:
   ```sh
   dotnet clean src/{Project}/{Project}.csproj -c Release
   dotnet pack  src/{Project}/{Project}.csproj -c Release \
     /p:ApiCompatGenerateSuppressionFile=true \
     /p:ApiCompatSuppressionOutputFile={unique-temp-path}
   ```
   Use the **final candidate version and the final baseline** — regenerating against a version you
   are about to change invalidates the result.
3. Compare the generated entries against the tracked file, by count and by content.

| Generated | Tracked | Meaning | Action |
|---|---|---|---|
| Empty | Non-empty | Every tracked entry is stale for this baseline | Clear or delete the tracked file |
| Non-empty | Matches | Suppressions are current | Leave them alone |
| Non-empty | Differs | Some entries stale, some breaks genuinely need suppressing | Reconcile entry by entry, and confirm each remaining break with the user |

4. After clearing stale entries, rerun the plain CI-equivalent pack with no generation flags, and
   require it to pass on its own:
   ```sh
   dotnet clean -c Release
   dotnet pack  -c Release
   ```

Reverting the baseline is the other valid resolution, and sometimes the better one — it keeps the
release diff minimal. Choose deliberately between "advance the baseline and clear the stale
suppressions" and "keep the existing baseline", rather than letting the choice be made by whichever
one silences the error first. Either way, the baseline is determined by what shipped, never selected
to make validation pass.

### Compatibility Suppressions

When intentional breaking changes are confirmed, create or update `CompatibilitySuppressions.xml` in the affected project directory — the conventional location, which is auto-discovered.

**A suppression file has three valid outcomes, not one.** Entries get *added* when a new intentional break needs suppressing, *retained* when they still describe a real break against the current baseline, and *cleared* when the baseline moved and they no longer describe anything. Treating the file as append-only is what turned 312 obsolete entries into a release-blocking failure that read as a mass breaking change. Preservation is the default only while the baseline holds still; once it moves, the [audit](#baseline-transition-suppression-audit) decides what stays, and removing entries it proves stale is the fix rather than a regression.

Do not use a tracked file as a template for what entries should look like — it may legitimately be empty, and its contents describe whatever baseline it was generated against, not yours. Generate entries instead.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Suppressions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Suppression>
    <DiagnosticId>CP0002</DiagnosticId>
    <Target>M:ModelContextProtocol.SomeType.SomeMethod(System.String)</Target>
    <Left>lib/net10.0/ModelContextProtocol.Core.dll</Left>
    <Right>lib/net10.0/ModelContextProtocol.Core.dll</Right>
    <IsBaselineSuppression>true</IsBaselineSuppression>
  </Suppression>
</Suppressions>
```

The exact suppression entries are generated by the pack command when it reports errors — copy the suggested suppression XML from the build output, or generate the file directly with `/p:ApiCompatGenerateSuppressionFile=true`. Remember that suppressions are needed **per target framework** (net10.0, net9.0, net8.0, netstandard2.0).

#### Wiring the suppression file

A `CompatibilitySuppressions.xml` sitting in the project directory is **auto-discovered**. That is
the convention this repo uses, and it needs no wiring at all. Do not add MSBuild properties or items
to point at a file that is already found by convention — duplicate or incorrect wiring is easy to
add while chasing a failure and hard to spot afterward, and it ships in the release commit.

If you do need an explicit path:

| Name | Kind | Use |
|---|---|---|
| `CompatibilitySuppressionFilePath` | **Property** | The supported way to point at a suppression file explicitly |
| `ApiCompatSuppressionFile` | **Item** | Not a property. Setting it via `/p:` does nothing |
| `ApiCompatSuppressionOutputFile` | **Property** | Where `ApiCompatGenerateSuppressionFile=true` writes its output |

Retaining an empty suppressions file is fine when you want to keep the file in place after clearing
stale entries. It must still be **valid XML** — an empty `<Suppressions>` root, not a zero-byte
file — and it must preserve the repository's byte conventions for these files, including the
UTF-8 BOM and the final newline. A file that differs only in BOM or trailing newline produces a
confusing diff and can trip tooling that round-trips it.

### Common Diagnostic IDs

| ID | Meaning |
|----|---------|
| CP0001 | Type or member exists in left but not in right (removed) |
| CP0002 | Member signature changed |
| CP0005 | Virtual member removed from unsealed type |
| CP0006 | Parameter or return type changed |
| CP0008 | Sealed type was previously unsealed |

See the [full diagnostic list](https://learn.microsoft.com/dotnet/fundamentals/package-validation/diagnostic-ids) for details.

## API Diff Report (ApiDiff)

The API diff report provides a human-readable summary of public API changes between the previous release and the new version. This is included in the release PR description alongside the ApiCompat results.

> **Important:** If the ApiDiff tool cannot be installed or fails to produce output, the release preparation process must **pause**. Do not fall back to a manual summary. Instead, present the error to the user and ask how to proceed. The user may choose to troubleshoot the tool, skip the API diff section, or abort the release preparation.

### Installing Microsoft.DotNet.ApiDiff.Tool

The `Microsoft.DotNet.ApiDiff.Tool` is published on the .NET **transport feed**, not on NuGet.org. The transport feed URL follows the pattern `https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet{MAJOR}-transport/nuget/v3/index.json`, where `{MAJOR}` is the major version of the .NET SDK from `global.json`.

1. **Determine the SDK major version** from `global.json`:
   ```sh
   # Read the SDK version — e.g. "10.0.100" → MAJOR is 10
   cat global.json
   ```

2. **Install the tool** globally with the `--prerelease` flag (required since the tool is only published as prerelease):
   ```sh
   dotnet tool install --global Microsoft.DotNet.ApiDiff.Tool \
     --prerelease \
     --add-source https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet{MAJOR}-transport/nuget/v3/index.json
   ```
   For example, with .NET SDK 10.x:
   ```sh
   dotnet tool install --global Microsoft.DotNet.ApiDiff.Tool \
     --prerelease \
     --add-source https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet10-transport/nuget/v3/index.json
   ```

3. **Verify installation**:
   ```sh
   dotnet apidiff --help
   ```

If the tool is already installed, update it with `dotnet tool update` using the same flags.

> **Reference:** The dotnet/core repo's [RunApiDiff.md](https://github.com/dotnet/core/blob/main/release-notes/RunApiDiff.md) documents this same tool and transport feed approach for generating API diff reports between .NET releases.

### Generating the API Diff

1. **Build the current version** in Release configuration:
   ```sh
   dotnet build -c Release
   ```

2. **Download the baseline packages** from NuGet:
   ```sh
   mkdir api-diff-temp && cd api-diff-temp
   dotnet new console
   dotnet add package ModelContextProtocol.Core --version {baseline-version}
   dotnet add package ModelContextProtocol --version {baseline-version}
   dotnet add package ModelContextProtocol.AspNetCore --version {baseline-version}
   dotnet restore
   ```

3. **Run the diff** for each package, comparing the baseline assembly against the current build. Use `dotnet apidiff` with `-l` (left/baseline) and `-r` (right/current):
   ```sh
   # ModelContextProtocol.Core
   dotnet apidiff \
     -l ~/.nuget/packages/modelcontextprotocol.core/{baseline-version}/lib/net10.0/ModelContextProtocol.Core.dll \
     -r ../artifacts/bin/ModelContextProtocol.Core/Release/net10.0/ModelContextProtocol.Core.dll

   # ModelContextProtocol
   dotnet apidiff \
     -l ~/.nuget/packages/modelcontextprotocol/{baseline-version}/lib/net10.0/ModelContextProtocol.dll \
     -r ../artifacts/bin/ModelContextProtocol/Release/net10.0/ModelContextProtocol.dll

   # ModelContextProtocol.AspNetCore
   dotnet apidiff \
     -l ~/.nuget/packages/modelcontextprotocol.aspnetcore/{baseline-version}/lib/net10.0/ModelContextProtocol.AspNetCore.dll \
     -r ../artifacts/bin/ModelContextProtocol.AspNetCore/Release/net10.0/ModelContextProtocol.AspNetCore.dll
   ```

   > **Note:** The exact CLI flags may vary by version. Run `dotnet apidiff --help` to confirm the available options. If the tool uses different argument names (e.g., `--left`/`--right`, `--before`/`--after`, or positional arguments), adapt accordingly.

4. **Capture the output** for each package and format as markdown fenced code blocks with `diff` syntax highlighting.

5. **Repeat for other target frameworks** if a more comprehensive report is desired (e.g., `net9.0`, `net8.0`, `netstandard2.0`). At minimum, diff the highest TFM (`net10.0`).

### Per-Package Reports

Generate separate reports for each SDK package:
- **ModelContextProtocol.Core** — the core library with minimal dependencies
- **ModelContextProtocol** — the main package with hosting and DI extensions
- **ModelContextProtocol.AspNetCore** — HTTP-based server implementations

### Cleanup

After generating the reports, delete any temporary files (downloaded baseline packages, generated API files, temp projects). These must not be committed.

## Presenting Results

### In the PR Description

Include both reports in the PR description under dedicated sections:

```markdown
---

## API Compatibility Report

✅ All packages pass API compatibility validation against v{baseline-version}.

_or_

⚠️ API compatibility issues detected (suppressions added for intentional breaks):

[Detailed ApiCompat output]

## API Diff Report

### ModelContextProtocol.Core

[Diff or table of changes]

### ModelContextProtocol

[Diff or table of changes]

### ModelContextProtocol.AspNetCore

[Diff or table of changes]
```

### In the User Summary (Step 12)

Present a condensed version for the user review. **Report every shipping package, and do not state
that ApiCompat passed without these four facts** — "passed" is not meaningful without knowing what
it was validated against and whether stale suppressions were masking or manufacturing the result:

| Package | Baseline | Generated entries | Retained / removed | Plain pack |
|---|---|---|---|---|
| {package} | {baseline} | 0 | 0 retained / 312 removed | ✅ |
| {package} | {baseline} | 0 | 0 / 0 | ✅ |

Enumerate the packable projects under `src/` rather than working from a remembered list; the set
grows. A package that does not participate in validation still gets a row, reporting why — a first
release has no baseline to compare against, and that is a fact worth stating rather than an absence
worth hiding.

- **Baseline** — the `PackageValidationBaselineVersion` actually used, and whether it changed during
  this release
- **Generated entries** — count from `ApiCompatGenerateSuppressionFile=true` at the final version
  and baseline
- **Retained / removed** — tracked suppressions kept versus cleared as stale
- **Plain pack** — result of the CI-equivalent `dotnet clean -c Release; dotnet pack -c Release`
  with no generation flags, which is the run CI will reproduce

Then the summary lines:

```
API Compatibility: ✅ All {n} packages pass against v{baseline} ({n} stale suppressions removed from {package})
API Diff: +12 additions, -2 removals, ~3 changes across all packages
```

If the baseline changed, or any suppression file was modified, say so explicitly and explain why.
A silent baseline or suppression edit is the kind of change that passes local validation and then
fails CI.
