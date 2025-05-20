using ModelContextProtocol.Client;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests.Transport;

public class StdioClientTransportTests
{
    [Fact]
    public async Task CreateAsync_ValidProcessInvalidServer_Throws()
    {
        string id = Guid.NewGuid().ToString("N");

        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new() { Command = "cmd", Arguments = ["/C", $"echo \"{id}\" >&2"] }) :
            new(new() { Command = "ls", Arguments = [id] });

        IOException e = await Assert.ThrowsAsync<IOException>(() => McpClientFactory.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains(id, e.ToString());
    }
}
