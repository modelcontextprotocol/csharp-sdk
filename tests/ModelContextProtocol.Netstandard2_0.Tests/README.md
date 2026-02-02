# ModelContextProtocol.Netstandard2_0.Tests

This test project provides cross-platform testing of the `netstandard2.0` build of `ModelContextProtocol.Core`.

## Purpose

The main test project (`ModelContextProtocol.Tests`) targets multiple frameworks including `net472`, which only runs on Windows. This creates a gap for developers on non-Windows platforms who want to test the `netstandard2.0` build.

This project solves that by:
1. Targeting `net8.0` (stable, cross-platform)
2. Forcing `ModelContextProtocol.Core` to use its `netstandard2.0` build via `SetTargetFramework`
3. Including all test source files from the main test project

## Key Features

- **Cross-platform**: Runs on Windows, Linux, and macOS using the modern .NET runtime
- **netstandard2.0 testing**: Verifies the netstandard2.0 build without requiring .NET Framework or mono
- **Shared tests**: Uses the same test source files as `ModelContextProtocol.Tests` to ensure consistency

## How It Works

The project uses the MSBuild `SetTargetFramework` property on the project reference to force the referenced project to build for a specific target framework:

```xml
<ProjectReference Include="..\..\src\ModelContextProtocol.Core\ModelContextProtocol.Core.csproj">
  <SetTargetFramework>TargetFramework=netstandard2.0</SetTargetFramework>
</ProjectReference>
```

This allows the test project (targeting `net8.0`) to reference and test the `netstandard2.0` build of `ModelContextProtocol.Core`.

## Running Tests

```bash
# Run all tests
dotnet test tests/ModelContextProtocol.Netstandard2_0.Tests

# Run with filter
dotnet test tests/ModelContextProtocol.Netstandard2_0.Tests --filter '(Execution!=Manual)'
```

## Reference

This pattern is based on the approach used by [googleapis/dotnet-genai](https://github.com/googleapis/dotnet-genai/blob/f0d9a3eb970e91293b806dac49853b68e4ddcdca/Google.GenAI.Tests/Netstandard2_0Tests/Google.GenAI.Netstandard2_0.Tests.csproj).
