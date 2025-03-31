using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Hosting;

/// <summary>
/// Hosted service for a multi-session (i.e. HTTP) MCP server.
/// </summary>
internal sealed class McpServerMultiSessionHostedService : BackgroundService
{
    private readonly IServerTransport _serverTransport;
    private readonly McpServerOptions _serverOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;

    public McpServerMultiSessionHostedService(
        IServerTransport serverTransport,
        IOptions<McpServerOptions> serverOptions,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider)
    {
        _serverTransport = serverTransport;
        _serverOptions = serverOptions.Value;
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await AcceptSessionAsync(stoppingToken).ConfigureAwait(false) is { } server)
        {
            // TODO: Track all running sessions and wait for all sessions to complete for graceful shutdown.
            _ = server.RunAsync(stoppingToken);
        }
    }

    private Task<IMcpServer> AcceptSessionAsync(CancellationToken cancellationToken)
        => McpServerFactory.AcceptAsync(_serverTransport, _serverOptions, _loggerFactory, _serviceProvider, cancellationToken);
}
