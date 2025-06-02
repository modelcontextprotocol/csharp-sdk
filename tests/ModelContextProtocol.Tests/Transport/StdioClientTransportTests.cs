using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests.Transport;

public class StdioClientTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Fact]
    public async Task CreateAsync_ValidProcessInvalidServer_Throws()
    {
        string id = Guid.NewGuid().ToString("N");

        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new() { Command = "cmd", Arguments = ["/C", $"echo \"{id}\" >&2"] }, LoggerFactory) :
            new(new() { Command = "ls", Arguments = [id] }, LoggerFactory);

        IOException e = await Assert.ThrowsAsync<IOException>(() => McpClientFactory.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains(id, e.ToString());
    }
}
