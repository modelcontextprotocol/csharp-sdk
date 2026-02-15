using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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
    [InlineData("elicitation-sep1034-client-defaults")]
    [InlineData("sse-retry")]
    [InlineData("auth/metadata-default")]
    [InlineData("auth/metadata-var1")]
    [InlineData("auth/metadata-var2")]
    [InlineData("auth/metadata-var3")]
    [InlineData("auth/basic-cimd")]
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

    // Backcompat: Legacy 2025-03-26 OAuth flows (no PRM, root-location metadata) we don't implement.
    // [InlineData("auth/2025-03-26-oauth-metadata-backcompat")]
    // [InlineData("auth/2025-03-26-oauth-endpoint-fallback")]

    // Extensions: Require ES256 JWT signing (private_key_jwt) and client_credentials grant support.
    // [InlineData("auth/client-credentials-jwt")]
    // [InlineData("auth/client-credentials-basic")]

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

        var startInfo = NodeHelpers.ConformanceTestStartInfo($"client --scenario {scenario} --command \"{conformanceClientPath} {scenario}\"");

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

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return (
                Success: false,
                Output: outputBuilder.ToString(),
                Error: errorBuilder.ToString() + "\nProcess timed out after 5 minutes and was killed."
            );
        }

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();
        var success = process.ExitCode == 0 || HasOnlyWarnings(output, error);

        return (
            Success: success,
            Output: output,
            Error: error
        );
    }

    /// <summary>
    /// Checks if the conformance test output indicates that all checks passed with only
    /// warnings (no actual failures). The conformance runner exits with code 1 for warnings,
    /// but warnings represent acceptable behavior (e.g., timing tolerances in CI environments).
    /// </summary>
    private static bool HasOnlyWarnings(string output, string error)
    {
        // The conformance runner outputs a summary line like:
        //   "Passed: 2/2, 0 failed, 1 warnings"
        // If there are 0 failures but warnings > 0, the test behavior is acceptable.
        var combined = output + error;
        var match = Regex.Match(combined, @"(\d+) failed, (\d+) warnings");
        return match.Success
            && match.Groups[1].Value == "0"
            && int.TryParse(match.Groups[2].Value, out var warnings)
            && warnings > 0;
    }
}
