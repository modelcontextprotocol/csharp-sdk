using System.Diagnostics;
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
        var processStartInfo = ProcessStartInfoUtilities.CreateOnPath(
            "docker",
            $"run -p {_port}:3001 --name {_containerName} --rm tzolov/mcp-everything-server:v1",
            redirectStandardInput: true,
            redirectStandardOutput: true,
            redirectStandardError: true,
            useShellExecute: false,
            createNoWindow: true);

        _ = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException($"Could not start process for {processStartInfo.FileName} with '{processStartInfo.Arguments}'.");

        // Wait for the server to start
        await Task.Delay(10000);
    }
    public async ValueTask DisposeAsync()
    {
        try
        {

            // Stop the container
            var stopInfo = ProcessStartInfoUtilities.CreateOnPath(
                "docker",
                $"stop {_containerName}",
                redirectStandardOutput: false,
                redirectStandardError: false,
                useShellExecute: false,
                createNoWindow: true);

            using var stopProcess = Process.Start(stopInfo)
                ?? throw new InvalidOperationException($"Could not stop process for {stopInfo.FileName} with '{stopInfo.Arguments}'.");
            await stopProcess.WaitForExitAsync(TimeSpan.FromSeconds(10));
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
            ProcessStartInfo processStartInfo = ProcessStartInfoUtilities.CreateOnPath(
                "docker",
                "info",
                redirectStandardOutput: false,
                redirectStandardError: false,
                useShellExecute: false,
                createNoWindow: true);

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