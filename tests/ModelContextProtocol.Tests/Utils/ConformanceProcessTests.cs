using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ModelContextProtocol.Tests.Utils;

public class ConformanceProcessTests
{
    // The runner starts a detached descendant that inherits its redirected stdout and stderr, then exits. The
    // descendant keeps both streams open for two minutes, longer than the test allows for.
    private const string LingeringDescendantScript =
        "const child = require('child_process').spawn(process.execPath, ['-e', 'setTimeout(() => {}, 120000)'], { stdio: 'inherit', detached: true, windowsHide: true }); " +
        "child.unref(); console.log('descendant ' + child.pid); console.log('runner exited');";

    // The runner starts a child that inherits its redirected stdout and stderr, and both keep running past the deadline.
    private const string RunningTreeScript =
        "const child = require('child_process').spawn(process.execPath, ['-e', 'setTimeout(() => {}, 120000)'], { stdio: 'inherit', windowsHide: true }); " +
        "console.log('descendant ' + child.pid); setTimeout(() => {}, 120000);";

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(17, true, false)]
    [InlineData(3, false, false)]
    public async Task ExitCodeCannotBeOverriddenByPrintedSummary(int exitCode, bool passingSummary, bool expectedSuccess)
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed.");
        var startInfo = CreateStartInfo(exitCode, passingSummary);

        var result = await NodeHelpers.RunServerConformanceAsync(
            string.Empty,
            cancellationToken: TestContext.Current.CancellationToken,
            processStartInfo: startInfo);

        Assert.Equal(expectedSuccess, result.Success);
        Assert.Contains(passingSummary ? "Passed: 3/3" : "Passed: 2/3", result.Output);
        Assert.Contains("synthetic stderr", result.Error);
        if (exitCode != 0)
        {
            Assert.Contains($"exited with code {exitCode}", result.Error);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task RunnerThatExitsWhileADescendantHoldsItsOutput_FailsWithinTheDeadline()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed.");
        var deadline = TimeSpan.FromSeconds(3);
        var stopwatch = Stopwatch.StartNew();

        var result = await NodeHelpers.RunServerConformanceAsync(
            string.Empty,
            cancellationToken: TestContext.Current.CancellationToken,
            processStartInfo: CreateNodeStartInfo(LingeringDescendantScript),
            timeout: deadline);

        stopwatch.Stop();
        using var descendant = FindDescendant(result.Output);
        try
        {
            Assert.False(result.Success);
            Assert.Contains("runner exited", result.Output);
            Assert.Contains("timed out after 3 seconds", result.Error);
            Assert.True(stopwatch.Elapsed < deadline + TimeSpan.FromSeconds(20), $"The helper returned after {stopwatch.Elapsed}.");

            // Still running and holding the streams, so only the deadline could have ended the wait.
            Assert.False(descendant.HasExited);
        }
        finally
        {
            if (!descendant.HasExited)
            {
                descendant.Kill();
            }
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task RunnerStillRunningAtTheDeadline_IsKilledWithItsChildren()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed.");
        var deadline = TimeSpan.FromSeconds(3);
        var stopwatch = Stopwatch.StartNew();

        var result = await NodeHelpers.RunServerConformanceAsync(
            string.Empty,
            cancellationToken: TestContext.Current.CancellationToken,
            processStartInfo: CreateNodeStartInfo(RunningTreeScript),
            timeout: deadline);

        stopwatch.Stop();
        Assert.False(result.Success);
        Assert.Contains("timed out after 3 seconds", result.Error);
        Assert.Contains("and was killed", result.Error);
        Assert.True(stopwatch.Elapsed < deadline + TimeSpan.FromSeconds(20), $"The helper returned after {stopwatch.Elapsed}.");

        // The child is not killed here: if the helper missed it, it ends on its own after two minutes.
        Assert.True(ExitsWithin(DescendantId(result.Output), TimeSpan.FromSeconds(10)), "The runner's child process is still running.");
    }

    private ProcessStartInfo CreateStartInfo(int exitCode, bool passingSummary)
    {
        var summary = passingSummary
            ? "Passed: 3/3, 0 failed, 0 warnings"
            : "Passed: 2/3, 1 failed, 0 warnings";
        return CreateNodeStartInfo($"console.log('Test Results:'); console.log('{summary}'); console.error('synthetic stderr'); process.exitCode = {exitCode};");
    }

    private ProcessStartInfo CreateNodeStartInfo(string script) => new()
    {
        FileName = "node",
        Arguments = $"-e \"{script}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    private Process FindDescendant(string runnerOutput) => Process.GetProcessById(DescendantId(runnerOutput));

    private int DescendantId(string runnerOutput)
    {
        var match = Regex.Match(runnerOutput, @"descendant (\d+)");
        Assert.True(match.Success, $"The runner did not report its descendant: {runnerOutput}");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private bool ExitsWithin(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // No process has that id anymore.
            return true;
        }
    }
}
