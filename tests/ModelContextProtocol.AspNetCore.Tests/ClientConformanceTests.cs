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

        // For sse-retry, allow warnings-only failures (timing can vary in CI)
        if (!result.Success && scenario == "sse-retry" && HasOnlyWarnings(result.Output))
        {
            // Skip the test with a message explaining that warnings are expected
            Assert.Skip(
                "sse-retry test has warnings but no failures. " +
                "Timing warnings are expected in resource-constrained CI environments. " +
                $"Details:\n{result.Output}");
        }

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

        return (
            Success: process.ExitCode == 0,
            Output: outputBuilder.ToString(),
            Error: errorBuilder.ToString()
        );
    }

    /// <summary>
    /// Checks if the conformance test output indicates only warnings (no actual test failures).
    /// The conformance test runner treats warnings as failures (exit code 1), but for timing-sensitive
    /// tests like sse-retry, we want to allow warnings in resource-constrained CI environments.
    /// </summary>
    /// <param name="output">The test output to check.</param>
    /// <returns>True if there are warnings but no failures, false otherwise.</returns>
    private static bool HasOnlyWarnings(string output)
    {
        // Look for the test results line: "Passed: X/Y, Z failed, W warnings"
        // If Z (failed count) is 0 and W (warnings count) is > 0, we have only warnings
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Look for "Passed: X/Y, Z failed, W warnings"
            if (trimmedLine.StartsWith("Passed:", StringComparison.OrdinalIgnoreCase))
            {
                // Extract failed count and warnings count
                // Example: "Passed: 2/2, 0 failed, 1 warnings"
                var parts = trimmedLine.Split(',');
                if (parts.Length >= 3)
                {
                    // Check for "0 failed"
                    var failedPart = parts[1].Trim();
                    if (failedPart.StartsWith("0 failed", StringComparison.OrdinalIgnoreCase))
                    {
                        // Check for at least "1 warnings"
                        var warningsPart = parts[2].Trim();
                        if (warningsPart.Contains("warning", StringComparison.OrdinalIgnoreCase))
                        {
                            // Extract warning count
                            var warningsMatch = System.Text.RegularExpressions.Regex.Match(
                                warningsPart, @"(\d+)\s+warning");
                            if (warningsMatch.Success && int.TryParse(warningsMatch.Groups[1].Value, out var warningCount))
                            {
                                return warningCount > 0;
                            }
                        }
                    }
                }
            }
        }
        
        return false;
    }
}
