using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using Microsoft.Extensions.Hosting;

namespace ModelContextProtocol.Hosting;

/// <summary>
/// Hosted service for the MCP server.
/// </summary>
public class McpServerHostedService : BackgroundService
{
    private readonly IMcpServer _server;

    /// <summary>
    /// Creates a new instance of the McpServerHostedService.
    /// </summary>
    /// <param name="server">The MCP server instance</param>
    /// <exception cref="ArgumentNullException"></exception>
    public McpServerHostedService(IMcpServer server)
    {
        Throw.IfNull(server);
     
        _server = server;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _server.RunAsync(cancellationToken: stoppingToken).ConfigureAwait(false);
    }
}
