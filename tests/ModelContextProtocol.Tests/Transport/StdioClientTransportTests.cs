using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;
using System.Runtime.InteropServices;
using System.Text;

namespace ModelContextProtocol.Tests.Transport;

public class StdioClientTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    public static bool IsStdErrCallbackSupported => !PlatformDetection.IsMonoRuntime;

    [Fact]
    public async Task CreateAsync_ValidProcessInvalidServer_Throws()
    {
        string id = Guid.NewGuid().ToString("N");

        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new() { Command = "cmd", Arguments = ["/C", $"echo \"{id}\" >&2"] }, LoggerFactory) :
            new(new() { Command = "ls", Arguments = [id] }, LoggerFactory);

        IOException e = await Assert.ThrowsAsync<IOException>(() => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Contains(id, e.ToString());
        }
    }

    [Fact(Skip = "Platform not supported by this test.", SkipUnless = nameof(IsStdErrCallbackSupported))]
    public async Task CreateAsync_ValidProcessInvalidServer_StdErrCallbackInvoked()
    {
        string id = Guid.NewGuid().ToString("N");

        int count = 0;
        StringBuilder sb = new();
        Action<string> stdErrCallback = line =>
        {
            Assert.NotNull(line);
            lock (sb)
            {
                sb.AppendLine(line);
                count++;
            }
        };

        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new() { Command = "cmd", Arguments = ["/C", $"echo \"{id}\" >&2"], StandardErrorLines = stdErrCallback }, LoggerFactory) :
            new(new() { Command = "ls", Arguments = [id], StandardErrorLines = stdErrCallback }, LoggerFactory);

        await Assert.ThrowsAsync<IOException>(() => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.InRange(count, 1, int.MaxValue);
        Assert.Contains(id, sb.ToString());
    }
}
