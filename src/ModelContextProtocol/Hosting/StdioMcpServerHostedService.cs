using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Hosting;

/// <summary>
/// Hosted service for a single-session (i.e stdio) MCP server.
/// </summary>
internal sealed class StdioMcpServerHostedService(IMcpServer session) : BackgroundService
{
    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => session.RunAsync(stoppingToken);
}
