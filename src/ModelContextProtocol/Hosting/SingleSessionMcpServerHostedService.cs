using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Hosting;

/// <summary>
/// Hosted service for a single-session (e.g. stdio) MCP server.
/// </summary>
internal sealed class SingleSessionMcpServerHostedService(IMcpServer session) : BackgroundService
{
    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => session.RunAsync(stoppingToken);
}
