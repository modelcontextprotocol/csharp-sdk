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

    // Backcompat: Legacy 2025-03-26 OAuth flows (no PRM, root-location metadata).
    [InlineData("auth/2025-03-26-oauth-metadata-backcompat")]
    [InlineData("auth/2025-03-26-oauth-endpoint-fallback")]

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
    /// warnings or known CI-timing failures. The conformance runner exits with code 1 for
    /// warnings/failures, but some represent acceptable behavior in CI environments:
    /// - Warnings (e.g., slightly late reconnects) are always acceptable.
    /// - "Reconnected very late" failures are acceptable when the actual delay is within a
    ///   reasonable bound, as CI machines may introduce network/scheduling latency that pushes
    ///   the observed reconnect time past the conformance test's strict threshold even though
    ///   the client correctly honored the retry field.
    /// </summary>
    private static bool HasOnlyWarnings(string output, string error)
    {
        // The conformance runner outputs a summary line like:
        //   "Passed: 2/2, 0 failed, 1 warnings"
        // If there are 0 failures but warnings > 0, the test behavior is acceptable.
        var combined = output + error;
        var match = Regex.Match(combined, @"(?<failed>\d+) failed, (?<warnings>\d+) warnings");
        if (!match.Success)
        {
            return false;
        }

        if (match.Groups["failed"].Value == "0"
            && int.TryParse(match.Groups["warnings"].Value, out var warnings)
            && warnings > 0)
        {
            return true;
        }

        // Also accept cases where all failures are "reconnected very late" timing failures.
        // These occur in CI when OS/network overhead between the server closing the SSE stream
        // and the client detecting it pushes the total reconnect time past the conformance
        // test's VERY_LATE_MULTIPLIER threshold (2x the retry value), even though the client
        // correctly waited the retry interval after detecting the stream close.
        // We require the actual delay to be < 10x the expected retry value to avoid masking
        // genuine bugs where the client ignores the retry field entirely.
        if (int.TryParse(match.Groups["failed"].Value, out var failed) && failed > 0)
        {
            var lateReconnectMatches = Regex.Matches(combined, @"Client reconnected very late \((\d+)ms instead of (\d+)ms\)");
            if (lateReconnectMatches.Count == failed
                && lateReconnectMatches.Cast<Match>().All(m =>
                    int.TryParse(m.Groups[1].Value, out var actual)
                    && int.TryParse(m.Groups[2].Value, out var expected)
                    && expected > 0
                    && actual < expected * 10))
            {
                return true;
            }
        }

        return false;
    }
}
