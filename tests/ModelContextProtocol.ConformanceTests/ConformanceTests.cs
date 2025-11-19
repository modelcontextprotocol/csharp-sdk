using System.Diagnostics;
using System.Text;
using Xunit;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Runs the official MCP conformance tests against the ConformanceServer.
/// This test starts the ConformanceServer, runs the Node.js-based conformance test suite,
/// and reports the results.
/// </summary>
public class ConformanceTests : IAsyncLifetime
{
    private const int ServerPort = 3001;
    private static readonly string ServerUrl = $"http://localhost:{ServerPort}";
    private Process? _serverProcess;

    public async ValueTask InitializeAsync()
    {
        // Start the ConformanceServer
        _serverProcess = StartConformanceServer();

        // Wait for server to be ready (retry for up to 30 seconds)
        var timeout = TimeSpan.FromSeconds(30);
        var stopwatch = Stopwatch.StartNew();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                // Try to connect to the MCP endpoint
                var response = await httpClient.GetAsync($"{ServerUrl}/mcp");
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

    public ValueTask DisposeAsync()
    {
        // Stop the server
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            _serverProcess.Kill(entireProcessTree: true);
            _serverProcess.WaitForExit(5000);
            _serverProcess.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    [Fact]
    [Trait("Execution", "Manual")] // Requires Node.js/npm to be installed
    public async Task RunConformanceTests()
    {
        // Run the conformance test suite
        var result = await RunNpxConformanceTests();

        // Report the results
        Assert.True(result.Success,
            $"Conformance tests failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }    private static Process StartConformanceServer()
    {
        // Find the ConformanceServer project directory
        var testProjectDir = AppContext.BaseDirectory;
        var conformanceServerDir = Path.GetFullPath(
            Path.Combine(testProjectDir, "..", "..", "..", "..", "ConformanceServer"));

        if (!Directory.Exists(conformanceServerDir))
        {
            throw new DirectoryNotFoundException(
                $"ConformanceServer directory not found at: {conformanceServerDir}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --project ConformanceServer.csproj --urls {ServerUrl}",
            WorkingDirectory = conformanceServerDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment =
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development"
            }
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start ConformanceServer process");
        }

        return process;
    }

    private static async Task<(bool Success, string Output, string Error)> RunNpxConformanceTests()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "npx",
            Arguments = $"@modelcontextprotocol/conformance server --url {ServerUrl}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                Console.WriteLine(e.Data); // Echo to test output
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                Console.Error.WriteLine(e.Data); // Echo to test output
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
