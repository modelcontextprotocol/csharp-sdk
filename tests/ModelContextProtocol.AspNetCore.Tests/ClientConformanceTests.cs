using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Runs the official MCP conformance tests against the ConformanceClient.
/// This test runs the Node.js-based conformance test suite for the client
/// and reports the results.
/// </summary>
public class ClientConformanceTests
{
    private readonly ITestOutputHelper _output;

    // Public static property required for SkipUnless attribute
    public static bool IsNodeInstalled => NodeHelpers.IsNodeInstalled();

    public ClientConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory(Skip = "Node.js is not installed. Skipping client conformance tests.", SkipUnless = nameof(IsNodeInstalled))]
    [InlineData("initialize")]
    [InlineData("tools_call")]
    [InlineData("auth/metadata-default")]
    [InlineData("auth/metadata-var1")]
    [InlineData("auth/metadata-var2")]
    [InlineData("auth/metadata-var3")]
    [InlineData("auth/basic-cimd")]
    // [InlineData("auth/2025-03-26-oauth-metadata-backcompat")]
    // [InlineData("auth/2025-03-26-oauth-endpoint-fallback")]
    [InlineData("auth/scope-from-www-authenticate")]
    [InlineData("auth/scope-from-scopes-supported")]
    [InlineData("auth/scope-omitted-when-undefined")]
    [InlineData("auth/scope-step-up")]
    public async Task RunConformanceTest(string scenario)
    {
        // Run the conformance test suite
        var result = await RunClientConformanceScenario(scenario);

        // Report the results
        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    private async Task<(bool Success, string Output, string Error)> RunClientConformanceScenario(string scenario)
    {
        // Construct an absolute path to the conformance client executable
        var exeSuffix = OperatingSystem.IsWindows() ? ".exe" : "";
        var conformanceClientPath = Path.GetFullPath($"./ModelContextProtocol.ConformanceClient{exeSuffix}");
        // Replace AspNetCore.Tests with ConformanceClient in the path
        conformanceClientPath = conformanceClientPath.Replace("AspNetCore.Tests", "ConformanceClient");

        if (!File.Exists(conformanceClientPath))
        {
            throw new FileNotFoundException(
                $"ConformanceClient executable not found at: {conformanceClientPath}");
        }

        var startInfo = NodeHelpers.NodeModulesBinStartInfo("conformance", $"client --scenario {scenario} --command \"{conformanceClientPath} {scenario}\"");

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                _output.WriteLine(e.Data);
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                _output.WriteLine(e.Data);
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        return (
            Success: process.ExitCode == 0,
            Output: outputBuilder.ToString(),
            Error: errorBuilder.ToString()
        );
    }
}
