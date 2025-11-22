using System.Diagnostics;
using System.Text;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Runs the official MCP conformance tests against the ConformanceServer.
/// This test starts the ConformanceServer, runs the Node.js-based conformance test suite,
/// and reports the results.
/// </summary>
public class ConformanceTests : IAsyncLifetime
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
    private Process? _serverProcess;

    public ConformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _serverUrl = $"http://localhost:{_serverPort}";
    }

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
                // Try to connect to the health endpoint
                var response = await httpClient.GetAsync($"{_serverUrl}/health");
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
    public async Task RunConformanceTests()
    {
        // Check if Node.js is installed
        Assert.SkipWhen(!IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");

        // Run the conformance test suite
        var result = await RunNpxConformanceTests();

        // Report the results
        Assert.True(result.Success,
            $"Conformance tests failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }    private Process StartConformanceServer()
    {
        // The ConformanceServer binary is in a parallel directory to the test binary
        // Test binary is in: artifacts/bin/ModelContextProtocol.ConformanceTests/Debug/{tfm}/
        // ConformanceServer binary is in: artifacts/bin/ModelContextProtocol.ConformanceServer/Debug/{tfm}/
        var testBinaryDir = AppContext.BaseDirectory; // e.g., .../net10.0/
        var targetFramework = Path.GetFileName(testBinaryDir.TrimEnd(Path.DirectorySeparatorChar));
        var conformanceServerDir = Path.GetFullPath(
            Path.Combine(testBinaryDir, "..", "..", "..", "ModelContextProtocol.ConformanceServer", "Debug", targetFramework));

        if (!Directory.Exists(conformanceServerDir))
        {
            throw new DirectoryNotFoundException(
                $"ConformanceServer directory not found at: {conformanceServerDir}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"ModelContextProtocol.ConformanceServer.dll --urls {_serverUrl}",
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

        // Asynchronously read output to prevent buffer overflow
        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                {
                    _output.WriteLine($"[Server {_serverPort}] {line}");
                }
            }
            catch { /* Process may exit */ }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync()) != null)
                {
                    _output.WriteLine($"[Server {_serverPort} ERROR] {line}");
                }
            }
            catch { /* Process may exit */ }
        });

        return process;
    }

    private async Task<(bool Success, string Output, string Error)> RunNpxConformanceTests()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "npx",
            Arguments = $"@modelcontextprotocol/conformance server --url {_serverUrl}",
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

    private static bool IsNodeInstalled()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
