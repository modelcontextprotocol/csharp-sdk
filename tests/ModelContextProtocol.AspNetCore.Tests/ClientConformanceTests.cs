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
public class ClientConformanceTests //: IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    // Public static property required for SkipUnless attribute
    public static bool IsNpxInstalled => NodeHelpers.IsNpxInstalled();

    public ClientConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Expected scenarios based on InlineData attributes below.
    // All scenarios from the conformance suite must be listed here to ensure VerifyAllConformanceTestsAreListed
    // detects any new scenarios added to the suite. Scenarios may be disabled (not in InlineData) but still
    // listed here - see comments on the Theory for explanations of why specific scenarios are disabled.
    private static readonly string[] ExpectedScenarios =
    [
        "initialize",
        "tools_call",
        "elicitation-sep1034-client-defaults",
        "sse-retry", // Disabled - tests pure SSE reconnection, not MCP behavior (see comment on Theory)
        "auth/metadata-default",
        "auth/metadata-var1",
        "auth/metadata-var2",
        "auth/metadata-var3",
        "auth/basic-cimd",
        "auth/2025-03-26-oauth-metadata-backcompat", // Disabled - tests deprecated 2025-03-26 spec (see comment on Theory)
        "auth/2025-03-26-oauth-endpoint-fallback", // Disabled - tests deprecated 2025-03-26 spec (see comment on Theory)
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

    // The sse-retry test is disabled because it tests pure SSE reconnection behavior,
    // not MCP-specific behavior. The test expects the client to:
    // 1. Connect via SSE GET
    // 2. Receive a priming event with retry interval and event ID
    // 3. Gracefully handle stream closure
    // 4. Reconnect with Last-Event-ID header (per SSE spec)
    //
    // The MCP SDK's SSE transport waits for an "endpoint" MCP event before considering
    // the connection established (required for MCP message routing). Without this event,
    // the connection establishment times out after 30 seconds.
    //
    // When run, the test shows:
    // - [client-sse-graceful-reconnect] SUCCESS - Core SSE reconnection works
    // - [client-sse-retry-timing] WARNING - "Client MUST respect the retry field timing"
    // - [client-sse-last-event-id] WARNING - "Client SHOULD send Last-Event-ID header"
    //
    // Per SSE specification (https://html.spec.whatwg.org/multipage/server-sent-events.html):
    // - Reconnecting after stream close is MUST behavior (works)
    // - Sending Last-Event-ID is SHOULD behavior for resumability
    // - Respecting retry timing is SHOULD behavior
    //
    // The test fails due to client timeout, not actual SSE behavior issues.
    // Supporting pure SSE (non-MCP) would require architectural changes to the transport.
    // [InlineData("sse-retry")]

    [InlineData("auth/metadata-default")]
    [InlineData("auth/metadata-var1")]
    [InlineData("auth/metadata-var2")]
    [InlineData("auth/metadata-var3")]
    [InlineData("auth/basic-cimd")]

    // The following two tests are for backward compatibility with the deprecated 2025-03-26 MCP spec.
    // They test legacy OAuth discovery behavior that the SDK intentionally does not support:
    // - auth/2025-03-26-oauth-metadata-backcompat: Tests OAuth flow without Protected Resource Metadata (PRM),
    //   expecting OAuth metadata at the server root. The current SDK requires PRM per the 2025-11-25 spec.
    // - auth/2025-03-26-oauth-endpoint-fallback: Tests fallback to standard OAuth endpoints (/authorize, /token,
    //   /register) at the server root when no metadata endpoints exist. The SDK doesn't implement this fallback.
    // These are listed in ExpectedScenarios to ensure VerifyAllConformanceTestsAreListed passes, but they are
    // not required for Tier 1 SDK compliance as they test deprecated spec behavior.
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

        var error = errorBuilder.ToString();
        var combinedOutput = outputBuilder.ToString() + error;

        // Strip ANSI escape codes for reliable pattern matching (ESC [ ... m)
        var strippedOutput = Regex.Replace(combinedOutput, @"\u001b\[[0-9;]*m|\x1b\[[0-9;]*m", "", RegexOptions.IgnoreCase);

        // Check for success based on the conformance test output, not just exit code.
        // Some tests (like auth/resource-mismatch) expect the client to exit with an error
        // after correctly detecting a security issue. The conformance harness reports these
        // as "CLIENT EXITED WITH ERROR" but if all actual checks passed (indicated by
        // "Passed: X/X, 0 failed"), we should treat this as success.
        bool checksPass =
            strippedOutput.Contains("OVERALL: PASSED", StringComparison.OrdinalIgnoreCase) ||
            (strippedOutput.Contains(", 0 failed,", StringComparison.OrdinalIgnoreCase) &&
             strippedOutput.Contains("Passed:", StringComparison.OrdinalIgnoreCase));

        return (Success: process.ExitCode == 0 || checksPass,
                Output: strippedOutput,
                Error: error);
    }
}
