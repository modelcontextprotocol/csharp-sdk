using System.Diagnostics;
using System.Runtime.InteropServices;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Shared fixture that starts a single ConformanceServer instance for all tests in
/// <see cref="ServerConformanceTests"/>. This avoids TCP port TIME_WAIT conflicts
/// that occur when each test starts and stops its own server on the same port.
/// </summary>
public class ConformanceServerFixture : IAsyncLifetime
{
    // Use different ports for each target framework to allow parallel execution
    // net10.0 -> 3001, net9.0 -> 3002, net8.0 -> 3003
    private static int GetPortForTargetFramework()
    {
        var testBinaryDir = AppContext.BaseDirectory;
        var targetFramework = Path.GetFileName(testBinaryDir.TrimEnd(Path.DirectorySeparatorChar));

        return targetFramework switch
        {
            "net10.0" => 3001,
            "net9.0" => 3002,
            "net8.0" => 3003,
            _ => 3001 // Default fallback
        };
    }

    private Task? _serverTask;
    private CancellationTokenSource? _serverCts;

    public string ServerUrl { get; } = $"http://localhost:{GetPortForTargetFramework()}";

    public async ValueTask InitializeAsync()
    {
        _serverCts = new CancellationTokenSource();
        // Explicitly pass "--stateless false" so this stateful fixture is immune to a globally
        // set MCP_CONFORMANCE_STATELESS environment variable (the command-line switch wins).
        _serverTask = Task.Run(() => ConformanceServer.Program.MainAsync(
            ["--urls", ServerUrl, "--stateless", "false"], cancellationToken: _serverCts.Token));

        // Wait for server to be ready (retry for up to 30 seconds)
        var timeout = TimeSpan.FromSeconds(30);
        var stopwatch = Stopwatch.StartNew();
        using var httpClient = new HttpClient { Timeout = TestConstants.HttpClientPollingTimeout };

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                await httpClient.GetAsync($"{ServerUrl}/health");
                return;
            }
            catch (HttpRequestException)
            {
                // Connection refused means server not ready yet
            }
            catch (TaskCanceledException)
            {
                // Timeout means server might be processing, give it more time
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException("ConformanceServer failed to start within the timeout period");
    }

    public async ValueTask DisposeAsync()
    {
        if (_serverCts != null)
        {
            _serverCts.Cancel();
            if (_serverTask != null)
            {
                try
                {
                    await _serverTask.WaitAsync(TestConstants.DefaultTimeout);
                }
                catch
                {
                    // Ignore exceptions during shutdown
                }
            }
            _serverCts.Dispose();
        }
    }
}

/// <summary>
/// Shared fixture that starts a single stateless ConformanceServer for the
/// SEP-2322 MRTR scenarios in <see cref="ServerConformanceTests"/>. Those scenarios negotiate the
/// 2026-07-28 revision, which is served only on a stateless server, so they
/// cannot reuse the stateful <see cref="ConformanceServerFixture"/>. Reusing one server across all
/// the MRTR theory rows avoids the TCP TIME_WAIT conflicts that per-test restarts on a single port
/// cause on Windows. Uses a dedicated port range (303x) so it runs in parallel with the stateful
/// fixture (300x), the caching server (301x), and the SEP-2243 servers (302x) without colliding.
/// </summary>
public sealed class StatelessMrtrConformanceServerFixture : IAsyncLifetime
{
    private StatelessConformanceServer? _server;

    public string ServerUrl => _server?.ServerUrl
        ?? throw new InvalidOperationException("The stateless conformance server has not been started.");

    public async ValueTask InitializeAsync()
    {
        _server = await StatelessConformanceServer.StartAsync(CancellationToken.None, basePort: 3031);
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }
}

/// <summary>
/// Runs the official MCP conformance tests against the ConformanceServer.
/// Uses a shared <see cref="ConformanceServerFixture"/> so the server is started once
/// and reused across all tests, avoiding TCP port conflicts on Windows.
/// </summary>
public class ServerConformanceTests(
    ConformanceServerFixture fixture,
    StatelessMrtrConformanceServerFixture statelessFixture,
    ITestOutputHelper output)
    : IClassFixture<ConformanceServerFixture>, IClassFixture<StatelessMrtrConformanceServerFixture>
{
    [Fact]
    public async Task RunConformanceTests()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");

        var result = await RunConformanceTestsAsync($"server --url {fixture.ServerUrl}");

        Assert.True(result.Success,
            $"Conformance tests failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    [Fact]
    public async Task RunPendingConformanceTest_JsonSchema202012()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        Assert.SkipWhen(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Pending Node-based conformance scenario is unstable on Windows due to a libuv shutdown assertion.");

        var result = await RunConformanceTestsAsync($"server --url {fixture.ServerUrl} --scenario json-schema-2020-12");

        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    [Fact]
    public async Task RunPendingConformanceTest_ServerSsePolling()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        Assert.SkipWhen(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Pending Node-based conformance scenario is unstable on Windows due to a libuv shutdown assertion.");

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

        // SEP-2243 is a 2026-07-28 protocol revision scenario that uses the stateless
        // lifecycle, so it requires a stateless server (a stateful server rejects the un-initialized list/call
        // requests with JSON-RPC -32000). Use a dedicated port range so it never collides with
        // the stateful class fixture (300x) or the caching stateless server (301x).
        await using var server = await StatelessConformanceServer.StartAsync(
            TestContext.Current.CancellationToken, basePort: 3021);

        var result = await RunStatelessConformanceTestAsync(
            $"server --url {server.ServerUrl} --scenario http-header-validation --spec-version 2026-07-28");

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

        await using var server = await StatelessConformanceServer.StartAsync(
            TestContext.Current.CancellationToken, basePort: 3024);

        var result = await RunStatelessConformanceTestAsync(
            $"server --url {server.ServerUrl} --scenario http-custom-header-server-validation --spec-version 2026-07-28");

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
    // served only on a stateless server, the scenarios run against a dedicated stateless server
    // (StatelessMrtrConformanceServerFixture); a stateful server refuses these requests.
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
            $"server --url {statelessFixture.ServerUrl} --scenario {scenario} --spec-version 2026-07-28");

        Assert.True(result.Success,
            $"MRTR conformance test '{scenario}' failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    private async Task<(bool Success, string Output, string Error)> RunConformanceTestsAsync(string arguments)
    {
        return await NodeHelpers.RunServerConformanceAsync(
            arguments,
            line => { try { output.WriteLine(line); } catch { } },
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
