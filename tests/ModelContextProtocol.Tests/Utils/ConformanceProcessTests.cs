using System.Diagnostics;

namespace ModelContextProtocol.Tests.Utils;

public class ConformanceProcessTests
{
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

    [Fact]
    public async Task InstalledRunnerListsAllRequiredSkillsScenarios()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed.");

        var result = await NodeHelpers.RunServerConformanceAsync(
            "list --server",
            appendProtocolVersionFromEnv: false,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, $"Runner failed.\n{result.Output}\n{result.Error}");
        Assert.Contains("sep-2640-skills-enumeration", result.Output);
        Assert.Contains("sep-2640-skills-manifest", result.Output);
        Assert.Contains("sep-2640-skills-directory", result.Output);
    }

    private ProcessStartInfo CreateStartInfo(int exitCode, bool passingSummary)
    {
        var summary = passingSummary
            ? "Passed: 3/3, 0 failed, 0 warnings"
            : "Passed: 2/3, 1 failed, 0 warnings";
        var script = $"console.log('Test Results:'); console.log('{summary}'); console.error('synthetic stderr'); process.exitCode = {exitCode};";
        return new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"-e \"{script}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }
}
