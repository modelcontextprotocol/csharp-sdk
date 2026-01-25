using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Runs the official MCP conformance tests against the ConformanceClient.
/// This test runs the Node.js-based conformance test suite for the client
/// and reports the results.
/// </summary>
public class ClientConformanceTests //: IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    // Public static property required for SkipUnless attribute
    public static bool IsNpxInstalled => NodeHelpers.IsNpxInstalled();

    public ClientConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Expected scenarios based on InlineData attributes below
    private static readonly string[] ExpectedScenarios = [
        "initialize",
        "tools_call",
        "elicitation-sep1034-client-defaults",
        "sse-retry",
        "auth/metadata-default",
        "auth/metadata-var1",
        "auth/metadata-var2",
        "auth/metadata-var3",
        "auth/basic-cimd",
        "auth/2025-03-26-oauth-metadata-backcompat", // Expected but not required to pass
        "auth/2025-03-26-oauth-endpoint-fallback", // Expected but not required to pass
        "auth/scope-from-www-authenticate",
        "auth/scope-from-scopes-supported",
        "auth/scope-omitted-when-undefined",
        "auth/scope-step-up",
        "auth/scope-retry-limit",
        "auth/token-endpoint-auth-basic",
        "auth/token-endpoint-auth-post",
        "auth/token-endpoint-auth-none",
        "auth/resource-mismatch",
        "auth/pre-registration",
        "auth/client-credentials-jwt",
        "auth/client-credentials-basic"
    ];

    [Fact(Skip = "npx is not installed. Skipping client conformance tests.", SkipUnless = nameof(IsNpxInstalled))]
    public async Task VerifyAllConformanceTestsAreListed()
    {
        // Get the list of available conformance tests from the suite
        var startInfo = NodeHelpers.NpxStartInfo("-y @modelcontextprotocol/conformance list --client");

        var outputBuilder = new StringBuilder();
        var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, "Failed to list conformance tests");

        var output = outputBuilder.ToString();
        var availableScenarios = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- "))
            .Select(line => line.Substring(2).Trim())
            .ToHashSet();

        // Verify all expected scenarios are available
        var missingScenarios = ExpectedScenarios.Except(availableScenarios).ToList();
        Assert.Empty(missingScenarios);

        // Verify we haven't missed any new scenarios
        var newScenarios = availableScenarios.Except(ExpectedScenarios).ToList();
        if (newScenarios.Any())
        {
            var newScenariosMessage = string.Join("\r\n  - ", newScenarios);
            Assert.Fail($"New conformance scenarios detected. Add these to ExpectedScenarios and the Theory:\r\n  - {newScenariosMessage}");
        }
    }

    [Theory(Skip = "npx is not installed. Skipping client conformance tests.", SkipUnless = nameof(IsNpxInstalled))]
    [InlineData("initialize")]
    [InlineData("tools_call")]
    [InlineData("elicitation-sep1034-client-defaults")]
    [InlineData("sse-retry")]
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
    [InlineData("auth/scope-retry-limit")]
    [InlineData("auth/token-endpoint-auth-basic")]
    [InlineData("auth/token-endpoint-auth-post")]
    [InlineData("auth/token-endpoint-auth-none")]
    [InlineData("auth/resource-mismatch")]
    [InlineData("auth/pre-registration")]
    [InlineData("auth/client-credentials-jwt")]
    [InlineData("auth/client-credentials-basic")]
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

        var startInfo = NodeHelpers.NpxStartInfo($"-y @modelcontextprotocol/conformance client --scenario {scenario} --command \"{conformanceClientPath} {scenario}\"");

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
