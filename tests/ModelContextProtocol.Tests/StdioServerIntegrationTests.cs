using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests;

public class StdioServerIntegrationTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    public static bool CanSendSigInt { get; } = (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) && !PlatformDetection.IsMonoRuntime;
    private const int SIGINT = 2;

    [Fact(Skip = "Platform not supported by this test.", SkipUnless = nameof(CanSendSigInt))]
    public async Task SigInt_DisposesTestServerWithHosting_Gracefully()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "TestServerWithHosting.dll",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();

        await using var streamServerTransport = new StreamServerTransport(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream,
            serverName: "TestServerWithHosting");

        await using var client = await McpClient.CreateAsync(
            new TestClientTransport(streamServerTransport),
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // I considered writing a similar test for Windows using Ctrl-C, then saw that dotnet watch doesn't even send a Ctrl-C
        // signal because it's such a pain without support for CREATE_NEW_PROCESS_GROUP in System.Diagnostics.Process.
        // https://github.com/dotnet/sdk/blob/43b1c12e3362098a23ca1018503eb56516840b6a/src/BuiltInTools/dotnet-watch/Internal/ProcessRunner.cs#L277-L303
        // https://github.com/dotnet/runtime/issues/109432, https://github.com/dotnet/runtime/issues/44944
        Assert.Equal(0, kill(process.Id, SIGINT));

        await process.WaitForExitAsync(TestConstants.DefaultTimeout);

        Assert.True(process.HasExited);
        Assert.Equal(0, process.ExitCode);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    private sealed class TestClientTransport(ITransport sessionTransport) : IClientTransport
    {
        public string Name => nameof(TestClientTransport);

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(sessionTransport);
    }
}
