using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Runs the official MCP conformance tests against the ConformanceClient.
/// This test runs the Node.js-based conformance test suite for the client
/// and reports the results.
/// </summary>
public class ClientConformanceTests //: IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    public ClientConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("initialize")]
    // [InlineData("tools_call")]
    public async Task RunConformanceTest(string scenario)
    {
        // Check if Node.js is installed
        Assert.SkipWhen(!IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");

        // Run the conformance test suite
        var result = await RunClientConformanceScenario(scenario);

        // Report the results
        Assert.True(result.Success,
            $"Conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }

    private async Task<(bool Success, string Output, string Error)> RunClientConformanceScenario(string scenario)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "npx",
            Arguments = $"-y @modelcontextprotocol/conformance client --scenario {scenario} --command \"dotnet run --no-build --project ../ModelContextProtocol.ConformanceClient/ModelContextProtocol.ConformanceClient.csproj\"",
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
                FileName = "npx",   // Check specifically for npx because windows seems unable to find it
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
