using ModelContextProtocol.Client;

namespace AspNetCoreMcpClient;

public sealed class McpClientConnection(McpClient client, HttpClientTransport transport) : IAsyncDisposable
{
    public McpClient Client { get; } = client;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Client.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }
}
