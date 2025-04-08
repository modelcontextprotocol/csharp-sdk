using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests;

public class StdioServerIntegrationTests
{
    public static bool CanSendSigInt { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    internal const int SIGINT = 2;

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

        // I considered writing a similar test for windows using Ctrl-C, then saw that dotnet watch doesn't even send a Ctrl-C
        // signal because it's such a pain without support CREATE_NEW_PROCESS_GROUP in System.Diagnostics.Process.
        // https://github.com/dotnet/sdk/blob/43b1c12e3362098a23ca1018503eb56516840b6a/src/BuiltInTools/dotnet-watch/Internal/ProcessRunner.cs#L277-L303
        // https://github.com/dotnet/runtime/issues/109432, https://github.com/dotnet/runtime/issues/44944
        Assert.Equal(0, kill(process.Id, SIGINT));

        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(shutdownCts.Token);

        Assert.True(process.HasExited);
        Assert.Equal(130, process.ExitCode);
    }

    [DllImport("libc", SetLastError = true)]
    internal static extern int kill(int pid, int sig);
}
