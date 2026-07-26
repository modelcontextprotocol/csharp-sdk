using System.Diagnostics;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Shared fixture that starts a single ConformanceServer exposing both the legacy stateful MCP
/// lifecycle (at <see cref="ServerUrl"/>, "/") and the SEP-2575 stateless lifecycle (at
/// <see cref="StatelessServerUrl"/>, "/stateless") on one port. A single long-lived server avoids
/// the TCP TIME_WAIT conflicts that per-test restarts on a fixed port cause on Windows, and
/// centralizes the port-binding logic that the stateful and stateless conformance tests previously
/// duplicated. Shared by <see cref="ServerConformanceTests"/> and <see cref="CachingConformanceTests"/>
/// via <see cref="ConformanceServerCollection"/>.
/// </summary>
public sealed class ConformanceServerFixture : IAsyncLifetime
{
    // Use different ports for each target framework to allow parallel execution across the
    // multi-targeted test processes. net10.0 -> 3001, net9.0 -> 3002, net8.0 -> 3003.
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

    /// <summary>Base URL of the stateful MCP endpoint (mapped at "/").</summary>
    public string ServerUrl { get; } = $"http://localhost:{GetPortForTargetFramework()}";

    /// <summary>
    /// URL of the stateless MCP endpoint (mapped at "/stateless"), used by the 2026-07-28
    /// scenarios (caching, MRTR, SEP-2243) that negotiate the stateless lifecycle.
    /// </summary>
    public string StatelessServerUrl => $"{ServerUrl}/stateless";

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
/// xUnit collection that shares one <see cref="ConformanceServerFixture"/> across the conformance
/// test classes so they run against a single server instance (and a single bound port).
/// </summary>
[CollectionDefinition(nameof(ConformanceServerCollection))]
public sealed class ConformanceServerCollection : ICollectionFixture<ConformanceServerFixture>;
