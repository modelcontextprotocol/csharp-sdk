using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

namespace ModelContextProtocol.Tests.Transport;

public class StdioClientTransportTests
{
    [Fact]
    public async Task CreateAsync_ValidProcessInvalidServer_Throws()
    {
        StdioClientTransport transport = new(new() { Command = "echo", Arguments = ["this is a test", "1>&2"] });

        IOException e = await Assert.ThrowsAsync<IOException>(() => McpClientFactory.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken));
        string exStr = e.ToString();
        if (!exStr.Contains("this is a test"))
        {
            throw new Exception($"Expected error message not found in exception: {exStr}");
        }
    }
}
