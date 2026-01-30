using System.Diagnostics;
using System.Net;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

public class EverythingSseServerFixture : IAsyncDisposable
{
    private readonly int _port;
    private readonly string _containerName;

    public static bool IsDockerAvailable => _isDockerAvailable ??= CheckIsDockerAvailable();
    private static bool? _isDockerAvailable;

    public EverythingSseServerFixture(int port)
    {
        _port = port;
        _containerName = $"mcp-everything-server-{_port}";
    }

    public async Task StartAsync()
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"run -p {_port}:3001 --name {_containerName} --rm tzolov/mcp-everything-server:v1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        _ = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException($"Could not start process for {processStartInfo.FileName} with '{processStartInfo.Arguments}'.");

        // Poll until the server is ready (up to 30 seconds)
        using var httpClient = new HttpClient { Timeout = TestConstants.HttpClientPollingTimeout };
        var endpoint = $"http://localhost:{_port}/sse";
        var deadline = DateTime.UtcNow.AddSeconds(30);
        
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.MethodNotAllowed)
                {
                    return;
                }
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
            {
                // server not ready
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"Docker container failed to start within 30 seconds on port {_port}");
    }
    public async ValueTask DisposeAsync()
    {
        try
        {

            // Stop the container
            var stopInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"stop {_containerName}",
                UseShellExecute = false
            };

            using var stopProcess = Process.Start(stopInfo)
                ?? throw new InvalidOperationException($"Could not stop process for {stopInfo.FileName} with '{stopInfo.Arguments}'.");
            await stopProcess.WaitForExitAsync(TestConstants.DefaultTimeout);
        }
        catch (Exception ex)
        {
            // Log the exception but don't throw
            await Console.Error.WriteLineAsync($"Error stopping Docker container: {ex.Message}");
        }
    }

    private static bool CheckIsDockerAvailable()
    {
#if NET
        try
        {
            ProcessStartInfo processStartInfo = new()
            {
                FileName = "docker",
                // "docker info" returns a non-zero exit code if docker engine is not running.
                Arguments = "info",
                UseShellExecute = false,
            };

            using var process = Process.Start(processStartInfo);
            process?.WaitForExit();
            return process?.ExitCode is 0;
        }
        catch
        {
            return false;
        }
#else
        // Do not run docker tests using .NET framework.
        return false;
#endif
    }
}