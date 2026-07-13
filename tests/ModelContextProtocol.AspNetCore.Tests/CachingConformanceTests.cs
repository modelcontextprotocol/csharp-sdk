using System.Diagnostics;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// A ConformanceServer instance started in the SEP-2575 stateless lifecycle, which the
/// SEP-2549 "caching" conformance scenario (new in the 2026-07-28 protocol revision)
/// requires. Started on demand (so it is not bound
/// when the caching test is skipped) and torn down via <see cref="DisposeAsync"/>. Uses a
/// distinct port range from the stateful <c>ConformanceServerFixture</c> (3001/3002/3003) so
/// the two can run in parallel without TCP conflicts.
/// </summary>
internal sealed class StatelessConformanceServer : IAsyncDisposable
{
    // Use different ports for each target framework to allow parallel execution across the
    // multi-targeted test processes, offset from a caller-supplied base port so independent
    // stateless servers (e.g. caching vs. SEP-2243) do not collide. net10.0 -> +0,
    // net9.0 -> +1, net8.0 -> +2.
    private static int GetPortForTargetFramework(int basePort)
    {
        var testBinaryDir = AppContext.BaseDirectory;
        var targetFramework = Path.GetFileName(testBinaryDir.TrimEnd(Path.DirectorySeparatorChar));

        var offset = targetFramework switch
        {
            "net10.0" => 0,
            "net9.0" => 1,
            "net8.0" => 2,
            _ => 0 // Default fallback
        };

        return basePort + offset;
    }

    private readonly Task _serverTask;
    private readonly CancellationTokenSource _serverCts;

    public string ServerUrl { get; }

    private StatelessConformanceServer(string serverUrl, Task serverTask, CancellationTokenSource serverCts)
    {
        ServerUrl = serverUrl;
        _serverTask = serverTask;
        _serverCts = serverCts;
    }

    public static async Task<StatelessConformanceServer> StartAsync(CancellationToken cancellationToken, int basePort = 3011)
    {
        var serverUrl = $"http://localhost:{GetPortForTargetFramework(basePort)}";
        var serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // "--stateless true" opts this server instance into the SEP-2575 stateless lifecycle
        // (see ConformanceServer.Program), without mutating process-wide environment state.
        var serverTask = Task.Run(() => ConformanceServer.Program.MainAsync(
            ["--urls", serverUrl, "--stateless", "true"], cancellationToken: serverCts.Token));

        // Wait for the server to be ready (retry for up to 30 seconds).
        var timeout = TimeSpan.FromSeconds(30);
        var stopwatch = Stopwatch.StartNew();
        using var httpClient = new HttpClient { Timeout = TestConstants.HttpClientPollingTimeout };

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                await httpClient.GetAsync($"{serverUrl}/health", cancellationToken);
                return new StatelessConformanceServer(serverUrl, serverTask, serverCts);
            }
            catch (HttpRequestException)
            {
                // Connection refused means the server is not ready yet.
            }
            catch (TaskCanceledException)
            {
                // Timeout means the server might be processing; give it more time.
            }

            await Task.Delay(500, cancellationToken);
        }

        serverCts.Cancel();
        serverCts.Dispose();
        throw new InvalidOperationException("Stateless ConformanceServer failed to start within the timeout period");
    }

    public async ValueTask DisposeAsync()
    {
        _serverCts.Cancel();
        try
        {
            await _serverTask.WaitAsync(TestConstants.DefaultTimeout);
        }
        catch
        {
            // Ignore exceptions during shutdown.
        }
        _serverCts.Dispose();
    }
}

/// <summary>
/// Runs the official MCP conformance "caching" scenario (SEP-2549: TTL for List Results,
/// added in conformance PR #275) against the SDK's ConformanceServer, verifying that the SDK
/// correctly emits the <c>ttlMs</c> and <c>cacheScope</c> caching hints on cacheable results
/// (tools/list, prompts/list, resources/list, resources/templates/list, resources/read).
/// </summary>
/// <remarks>
/// The scenario was introduced in spec wire version 2026-07-28 and uses the stateless lifecycle.
/// It is gated on the installed conformance
/// package's scenario list. The stateless server is
/// started only after the gates pass, so a skipped run binds no port.
/// </remarks>
public class CachingConformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RunCachingConformanceTest()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        Assert.SkipWhen(
            !NodeHelpers.HasCachingScenario(),
            "SEP-2549 caching conformance scenario is not available in the installed conformance package.");

        await using var server = await StatelessConformanceServer.StartAsync(TestContext.Current.CancellationToken);

        // The caching scenario only exists in the 2026-07-28 protocol revision, so pin the spec version
        // explicitly (and suppress the MCP_CONFORMANCE_PROTOCOL_VERSION override to avoid a
        // conflicting duplicate --spec-version flag).
        var result = await NodeHelpers.RunServerConformanceAsync(
            $"server --url {server.ServerUrl} --scenario caching --spec-version 2026-07-28",
            line => { try { output.WriteLine(line); } catch { } },
            appendProtocolVersionFromEnv: false,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success,
            $"SEP-2549 caching conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }
}
