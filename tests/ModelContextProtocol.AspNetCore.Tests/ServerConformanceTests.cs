using ModelContextProtocol.Tests.Utils;
using System.Text.RegularExpressions;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Runs the official MCP conformance tests against the ConformanceServer. Uses a shared
/// <see cref="ConformanceServerFixture"/> so the server is started once and reused across all
/// tests, avoiding TCP port conflicts on Windows. Stateful scenarios use the server's "/" endpoint
/// (<see cref="ConformanceServerFixture.ServerUrl"/>); 2026-07-28 scenarios that negotiate the
/// stateless lifecycle use its "/stateless" endpoint
/// (<see cref="ConformanceServerFixture.StatelessServerUrl"/>).
/// </summary>
[Collection(nameof(ConformanceServerCollection))]
public class ServerConformanceTests(
    ConformanceServerFixture fixture,
    ITestOutputHelper output)
{
    [Theory]
    [InlineData("sep-2640-skills-enumeration", "2026-07-28")]
    [InlineData("sep-2640-skills-manifest", "2026-07-28")]
    [InlineData("sep-2640-skills-directory", "2026-07-28")]
    [InlineData("sep-2640-skills-enumeration", "2025-11-25")]
    [InlineData("sep-2640-skills-manifest", "2025-11-25")]
    [InlineData("sep-2640-skills-directory", "2025-11-25")]
    public async Task RunSkillsConformanceTest(string scenario, string protocolVersion)
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        var url = protocolVersion == "2026-07-28" ? fixture.StatelessServerUrl : fixture.ServerUrl;

        var result = await RunConformanceTestsAsync(
            $"server --url {url} --scenario {scenario} --spec-version {protocolVersion} --force",
            appendProtocolVersionFromEnv: false);

        Assert.True(result.Success,
            $"Skills scenario '{scenario}' for '{protocolVersion}' failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
        Assert.DoesNotContain("SKIPPED: scenario", result.Output);
        AssertSkillsSummary(result.Output);
    }

    [Theory]
    [InlineData("Passed: 1/1, 0 failed", true)]
    [InlineData("Test Results:\nPassed: 32/32, 0 failed, 0 warnings\n", true)]
    [InlineData("Test Results:\r\n  Passed:\t6/6, 0 failed, 0 warnings\r\n", true)]
    [InlineData("Passed: 0/0, 0 failed", false)]
    [InlineData("Passed: 1/2, 0 failed", false)]
    [InlineData("Passed: 2/2, 1 failed", false)]
    [InlineData("Log: Passed: 1/1, 0 failed", false)]
    [InlineData("Passed: 1/1, 0 failed unexpectedly", false)]
    [InlineData("Passed: 1/1, 0 failed\nPassed: 0/0, 0 failed", false)]
    [InlineData("", false)]
    public void SkillsSummaryRequiresExactlyOnePositiveSuccessfulSummary(string output, bool valid)
    {
        if (valid)
        {
            AssertSkillsSummary(output);
        }
        else
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertSkillsSummary(output));
        }
    }

    [Fact]
    public async Task RunConformanceTests()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");

        var result = await RunConformanceTestsAsync($"server --url {fixture.ServerUrl}");

        Assert.True(result.Success,
            $"Conformance tests failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    [Fact]
    public async Task RunConformanceTest_JsonSchema202012()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");

        var result = await RunConformanceTestsAsync($"server --url {fixture.ServerUrl} --scenario json-schema-2020-12");

        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    [Fact]
    public async Task RunConformanceTest_ServerSsePolling()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");

        var result = await RunConformanceTestsAsync($"server --url {fixture.ServerUrl} --scenario server-sse-polling");

        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    [Fact]
    public async Task RunConformanceTest_HttpHeaderValidation()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        Assert.SkipWhen(
            !NodeHelpers.HasSep2243Scenarios(),
            "SEP-2243 conformance scenarios are not available in the installed conformance package.");

        // SEP-2243 is a 2026-07-28 protocol revision scenario that uses the stateless lifecycle,
        // so it runs against the shared server's stateless endpoint (a stateful server rejects the
        // un-initialized list/call requests with JSON-RPC -32000).
        var result = await RunStatelessConformanceTestAsync(
            $"server --url {fixture.StatelessServerUrl} --scenario http-header-validation --spec-version 2026-07-28");

        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    [Fact]
    public async Task RunConformanceTest_HttpCustomHeaderServerValidation()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        Assert.SkipWhen(
            !NodeHelpers.HasSep2243Scenarios(),
            "SEP-2243 conformance scenarios are not available in the installed conformance package.");

        var result = await RunStatelessConformanceTestAsync(
            $"server --url {fixture.StatelessServerUrl} --scenario http-custom-header-server-validation --spec-version 2026-07-28");

        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    // SEP-2322 (Multi Round-Trip Requests / InputRequiredResult) conformance scenarios.
    // The csharp-sdk ConformanceServer surfaces the matching tools/prompts via
    // ConformanceServer.Tools.IncompleteResultTools and ConformanceServer.Prompts.IncompleteResultPrompts
    // (the class names predate the conformance-suite rename from "incomplete-result-*" to
    // "input-required-result-*"; the wire-level tool names now match the new convention).
    // Each scenario uses the conformance harness's RawMcpSession, which negotiates 2026-07-28,
    // so the csharp-sdk emits InputRequiredResult on the wire. Because the 2026-07-28 revision is
    // served only on a stateless server, the scenarios run against the shared server's stateless
    // endpoint (ConformanceServerFixture.StatelessServerUrl); a stateful server refuses these requests.
    // These tests skip until the installed conformance package ships SEP-2322 scenarios
    // (see <see cref="NodeHelpers.HasMrtrScenarios"/>).
    //
    // input-required-result-tampered-state and input-required-result-capability-check are
    // implemented by ConformanceServer.Tools.IncompleteResultTools.ToolWithTamperedState
    // (HMAC-protected requestState; a tampered requestState surfaces a -32602 JSON-RPC error)
    // and ToolWithCapabilityCheck (gates inputRequests on the per-request
    // _meta clientCapabilities envelope). Both behaviors also have in-process wire-level
    // regression coverage in MrtrProtocolTests so they stay verified independent of the
    // published conformance package.
    [Theory]
    [InlineData("input-required-result-basic-elicitation")]
    [InlineData("input-required-result-basic-sampling")]
    [InlineData("input-required-result-basic-list-roots")]
    [InlineData("input-required-result-request-state")]
    [InlineData("input-required-result-multiple-input-requests")]
    [InlineData("input-required-result-multi-round")]
    [InlineData("input-required-result-missing-input-response")]
    [InlineData("input-required-result-non-tool-request")]
    [InlineData("input-required-result-result-type")]
    [InlineData("input-required-result-unsupported-methods")]
    [InlineData("input-required-result-tampered-state")]
    [InlineData("input-required-result-capability-check")]
    [InlineData("input-required-result-ignore-extra-params")]
    [InlineData("input-required-result-validate-input")]
    public async Task RunMrtrConformanceTest(string scenario)
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        Assert.SkipWhen(!NodeHelpers.HasMrtrScenarios(), "SEP-2322 MRTR conformance scenarios are not available in the installed conformance package.");

        var result = await RunStatelessConformanceTestAsync(
            $"server --url {fixture.StatelessServerUrl} --scenario {scenario} --spec-version 2026-07-28");

        Assert.True(result.Success,
            $"MRTR conformance test '{scenario}' failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    [Theory]
    [InlineData("tasks-wire-fields")]
    [InlineData("tasks-lifecycle")]
    [InlineData("tasks-capability-negotiation")]
    [InlineData("tasks-request-headers")]
    [InlineData("tasks-dispatch-and-envelope")]
    [InlineData("tasks-status-notifications")]
    [InlineData("tasks-required-task-error")]
    [InlineData("tasks-request-state-removal")]
    [InlineData("tasks-mrtr-input")]
    [InlineData("tasks-mrtr-composition")]
    public async Task RunTasksExtensionConformanceTest(string scenario)
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        Assert.SkipWhen(
            !NodeHelpers.HasTasksExtensionScenarios(),
            "SEP-2663 Tasks extension scenarios are not available in the installed conformance package.");

        var result = await RunStatelessConformanceTestAsync(
            $"server --url {fixture.StatelessServerUrl} --scenario {scenario}");

        Assert.True(result.Success,
            $"Tasks extension conformance test '{scenario}' failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    private void AssertSkillsSummary(string output)
    {
        var pattern = new Regex(
            @"^[ \t]*Passed:[ \t]*(?<passed>0|[1-9][0-9]*)/(?<total>0|[1-9][0-9]*),[ \t]*(?<failed>0|[1-9][0-9]*)[ \t]+failed(?:,[^\r\n]*)?\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromSeconds(1));
        var summary = Assert.Single(pattern.Matches(output).Cast<Match>());
        Assert.NotEqual("0", summary.Groups["passed"].Value);
        Assert.Equal(summary.Groups["total"].Value, summary.Groups["passed"].Value);
        Assert.Equal("0", summary.Groups["failed"].Value);
    }

    private async Task<(bool Success, string Output, string Error)> RunConformanceTestsAsync(
        string arguments, bool appendProtocolVersionFromEnv = true)
    {
        return await NodeHelpers.RunServerConformanceAsync(
            arguments,
            line => { try { output.WriteLine(line); } catch { } },
            appendProtocolVersionFromEnv: appendProtocolVersionFromEnv,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    // For 2026-07-28 protocol scenarios that pin --spec-version explicitly, suppress the
    // MCP_CONFORMANCE_PROTOCOL_VERSION override so a duplicate --spec-version is not appended.
    private async Task<(bool Success, string Output, string Error)> RunStatelessConformanceTestAsync(string arguments)
    {
        return await NodeHelpers.RunServerConformanceAsync(
            arguments,
            line => { try { output.WriteLine(line); } catch { } },
            appendProtocolVersionFromEnv: false,
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
