# README Code Sample Validation

This reference describes how to validate that C# code samples in README files compile against the current SDK.

## Which READMEs to Validate

Validate code samples from the **package README** files — these are shipped with NuGet packages and are the primary documentation users see:

| README | Package |
|--------|---------|
| `README.md` (root) | ModelContextProtocol |
| `src/ModelContextProtocol.Core/README.md` | ModelContextProtocol.Core |
| `src/ModelContextProtocol.AspNetCore/README.md` | ModelContextProtocol.AspNetCore |

Sample README files (`samples/*/README.md`) are excluded — the samples themselves are buildable projects and are validated by CI.

## What to Extract

Extract only fenced code blocks tagged as `csharp` (` ```csharp `). Skip blocks tagged as plain ` ``` ` (shell commands, install instructions) or any other language.

### Handling Incomplete Snippets

README samples are often **incomplete** — they use `...` for placeholder values, omit `using` directives, or show only a method body. The validation wrapper must account for this:

- **Placeholder expressions** like `IChatClient chatClient = ...;` — replace `...` on the right-hand side of assignments with `null!`
- **Missing usings** — the wrapper file supplies all common namespaces (see template below)
- **Top-level statements** — wrap in an `async Task` method so `await` works
- **Suppressed warnings** — disable CS1998 (async without await), CS8321 (unused local function), and similar non-substantive warnings

### Snippets That Cannot Be Validated

Some code blocks are illustrative fragments that cannot compile even with wrappers (e.g., partial class definitions shown in isolation, pseudo-code). If a snippet fails to compile after applying the standard fixups, examine the error:

- **API mismatch** (missing member, wrong type, wrong signature) → this is a **real bug** in the README that must be reported and fixed
- **Structural issue** (missing context, incomplete fragment) → exclude this specific snippet from validation with a comment explaining why

## Test Project Approach

Create a **temporary** test project that references the SDK projects, wraps each README's code samples in compilable methods, and builds.

### Project File Template

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>preview</LangVersion>
    <NoWarn>CS1998;CS8321;CS0168;CS0219;CS1591;CS8602</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ModelContextProtocol\ModelContextProtocol.csproj" />
    <ProjectReference Include="..\..\src\ModelContextProtocol.AspNetCore\ModelContextProtocol.AspNetCore.csproj" />
  </ItemGroup>
</Project>
```

Place the project at `tests/ReadmeSnippetValidation/ReadmeSnippetValidation.csproj`.

### Source File Template

Create one `.cs` file per README. Each file wraps the README's code blocks in static methods inside a class. Use this pattern:

```csharp
#pragma warning disable CS1998
#pragma warning disable CS8321

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace ReadmeSnippetValidation;

public static class RootReadmeSamples
{
    // Snippet 1: Client example
    public static async Task ClientExample()
    {
        // ... pasted snippet code ...
    }

    // Snippet 2: Server example
    public static async Task ServerExample()
    {
        // ... pasted snippet code ...
    }

    // Snippet with attributed types (tools, prompts) go as nested or sibling types
    [McpServerToolType]
    public static class EchoTool
    {
        [McpServerTool, Description("Echoes the message back to the client.")]
        public static string Echo(string message) => $"hello {message}";
    }
}
```

### Build Commands

```sh
# Restore and build the validation project
dotnet restore tests/ReadmeSnippetValidation/ReadmeSnippetValidation.csproj
dotnet build tests/ReadmeSnippetValidation/ReadmeSnippetValidation.csproj

### Cleanup

After validation, **always delete** the `tests/ReadmeSnippetValidation/` directory. It must not be committed.

## Reporting Results

### All Snippets Compile

Report success and move to the next step:
> ✅ All README code samples compile successfully against the current SDK.

### Compilation Failures

For each failure:
1. Identify the README file and which code block failed
2. Show the compiler error
3. Classify the error:
   - **API mismatch**: The README uses an API that doesn't exist or has a different signature. This indicates the README is outdated and needs updating.
   - **Structural**: The snippet is a fragment that can't be wrapped. Note it for exclusion.
4. For API mismatches, investigate the correct current API and propose a fix
5. Present all findings to the user for confirmation before making any README edits

### Fixing Issues

If the user approves fixes:
1. Edit the README files directly with minimal, surgical changes
2. Re-run the validation build to confirm fixes compile
3. Ensure the overall solution still builds: `dotnet build`
4. Include the README fixes in the same release or as a prerequisite PR — the user decides
