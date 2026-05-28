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
/// Runs the official MCP conformance tests against the ConformanceServer.
/// Uses a shared <see cref="ConformanceServerFixture"/> so the server is started once
/// and reused across all tests, avoiding TCP port conflicts on Windows.
/// </summary>
public class ServerConformanceTests(ConformanceServerFixture fixture, ITestOutputHelper output)
    : IClassFixture<ConformanceServerFixture>
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
            "SEP-2243 conformance scenarios not available (requires conformance package >= 0.2.0).");

        // SEP-2243 is a draft (DRAFT-2026-v1) scenario that uses the stateless lifecycle, so it
        // requires a stateless server (a stateful server rejects the un-initialized list/call
        // requests with JSON-RPC -32000). Use a dedicated port range so it never collides with
        // the stateful class fixture (300x) or the caching stateless server (301x).
        await using var server = await StatelessConformanceServer.StartAsync(
            TestContext.Current.CancellationToken, basePort: 3021);

        var result = await RunStatelessConformanceTestAsync(
            $"server --url {server.ServerUrl} --scenario http-header-validation --spec-version DRAFT-2026-v1");

        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    [Fact]
    public async Task RunConformanceTest_HttpCustomHeaderServerValidation()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        Assert.SkipWhen(
            !NodeHelpers.HasSep2243Scenarios(),
            "SEP-2243 conformance scenarios not available (requires conformance package >= 0.2.0).");

        await using var server = await StatelessConformanceServer.StartAsync(
            TestContext.Current.CancellationToken, basePort: 3024);

        var result = await RunStatelessConformanceTestAsync(
            $"server --url {server.ServerUrl} --scenario http-custom-header-server-validation --spec-version DRAFT-2026-v1");

        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    // SEP-2322 (Multi Round-Trip Requests / IncompleteResult) conformance scenarios.
    // The csharp-sdk ConformanceServer surfaces the matching tools/prompts via
    // ConformanceServer.Tools.IncompleteResultTools and ConformanceServer.Prompts.IncompleteResultPrompts.
    // Each scenario uses the conformance harness's RawMcpSession, which negotiates 2026-07-28
    // so the csharp-sdk emits InputRequiredResult on the wire. These tests skip until the
    // upstream conformance package ships with SEP-2322 scenarios
    // (https://github.com/modelcontextprotocol/conformance/pull/188).
    [Theory]
    [InlineData("incomplete-result-basic-elicitation")]
    [InlineData("incomplete-result-basic-sampling")]
    [InlineData("incomplete-result-basic-list-roots")]
    [InlineData("incomplete-result-request-state")]
    [InlineData("incomplete-result-multiple-input-requests")]
    [InlineData("incomplete-result-multi-round")]
    [InlineData("incomplete-result-missing-input-response")]
    [InlineData("incomplete-result-non-tool-request")]
    public async Task RunMrtrConformanceTest(string scenario)
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        Assert.SkipWhen(!NodeHelpers.HasMrtrScenarios(), "SEP-2322 MRTR conformance scenarios not yet available in the published @modelcontextprotocol/conformance package.");

        var result = await RunConformanceTestsAsync(
            $"server --url {fixture.ServerUrl} --scenario {scenario}");

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

    // For draft scenarios that pin --spec-version explicitly, suppress the
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
