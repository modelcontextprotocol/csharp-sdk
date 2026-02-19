using System.Diagnostics;
using System.Text;
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
        _serverTask = Task.Run(() => ConformanceServer.Program.MainAsync(
            ["--urls", ServerUrl], cancellationToken: _serverCts.Token));

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

        var result = await RunConformanceTestsAsync($"server --url {fixture.ServerUrl} --scenario json-schema-2020-12");

        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    [Fact]
    public async Task RunPendingConformanceTest_ServerSsePolling()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");

        var result = await RunConformanceTestsAsync($"server --url {fixture.ServerUrl} --scenario server-sse-polling");

        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    private async Task<(bool Success, string Output, string Error)> RunConformanceTestsAsync(string arguments)
    {
        var startInfo = NodeHelpers.ConformanceTestStartInfo(arguments);

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                output.WriteLine(e.Data);
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                output.WriteLine(e.Data);
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
}
