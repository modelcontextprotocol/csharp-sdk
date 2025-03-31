using Microsoft.Extensions.Logging;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Implements the MCP transport protocol over standard input/output streams.
/// </summary>
public sealed class StdioClientTransport : IClientTransport
{
    private readonly StdioClientTransportOptions _options;
    private readonly McpServerConfig _serverConfig;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the StdioTransport class.
    /// </summary>
    /// <param name="options">Configuration options for the transport.</param>
    /// <param name="serverConfig">The server configuration for the transport.</param>
    /// <param name="loggerFactory">A logger factory for creating loggers.</param>
    public StdioClientTransport(StdioClientTransportOptions options, McpServerConfig serverConfig, ILoggerFactory? loggerFactory = null)
    {
        Throw.IfNull(options);
        Throw.IfNull(serverConfig);

        _options = options;
        _serverConfig = serverConfig;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var streamTransport = new StdioClientStreamTransport(_options, _serverConfig, _loggerFactory);

        try
        {
            await streamTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return streamTransport;
        }
        catch
        {
            await streamTransport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
