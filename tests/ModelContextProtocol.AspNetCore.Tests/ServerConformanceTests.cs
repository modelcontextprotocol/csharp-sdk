using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Runs the official MCP conformance tests against the ConformanceServer.
/// This test starts the ConformanceServer, runs the Node.js-based conformance test suite,
/// and reports the results.
/// </summary>
public class ServerConformanceTests : IAsyncLifetime
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

    private readonly int _serverPort = GetPortForTargetFramework();
    private readonly string _serverUrl;
    private readonly ITestOutputHelper _output;
    private Task? _serverTask;
    private CancellationTokenSource? _serverCts;

    public ServerConformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _serverUrl = $"http://localhost:{_serverPort}";
    }

    public async ValueTask InitializeAsync()
    {
        // Start the ConformanceServer
        StartConformanceServer();

        // Wait for server to be ready (retry for up to 30 seconds)
        var timeout = TimeSpan.FromSeconds(30);
        var stopwatch = Stopwatch.StartNew();
        using var httpClient = new HttpClient { Timeout = TestConstants.HttpClientPollingTimeout };

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                // Try to connect to the health endpoint
                await httpClient.GetAsync($"{_serverUrl}/health");
                // Any response (even an error) means the server is up
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
        // Stop the server
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

    [Fact]
    public async Task RunConformanceTests()
    {
        // Check if Node.js is installed
        Assert.SkipWhen(!NodeHelpers.IsNpxInstalled(), "Node.js is not installed. Skipping conformance tests.");

        // Run the conformance test suite
        var result = await RunNpxConformanceTests();

        // Report the results
        Assert.True(result.Success,
            $"Conformance tests failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    private void StartConformanceServer()
    {
        // Start the server in a background task
        _serverCts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ConformanceServer.Program.MainAsync(["--urls", _serverUrl], new XunitLoggerProvider(_output), cancellationToken: _serverCts.Token));
    }

    private static string GetConformanceVersion()
    {
        var assembly = typeof(ServerConformanceTests).Assembly;
        var attribute = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "McpConformanceVersion");
        return attribute?.Value ?? throw new InvalidOperationException("McpConformanceVersion not found in assembly metadata");
    }

    private async Task<(bool Success, string Output, string Error)> RunNpxConformanceTests()
    {
        // Version is configured in Directory.Packages.props for central management
        var version = GetConformanceVersion();

        var startInfo = NodeHelpers.NpxStartInfo($"-y @modelcontextprotocol/conformance@{version} server --url {_serverUrl}");

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

        return (
            Success: process.ExitCode == 0,
            Output: outputBuilder.ToString(),
            Error: errorBuilder.ToString()
        );
    }
}
