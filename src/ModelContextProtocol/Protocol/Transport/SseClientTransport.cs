using Microsoft.Extensions.Logging;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// The ServerSideEvents client transport implementation
/// </summary>
public sealed class SseClientTransport : IClientTransport, IAsyncDisposable
{
    private readonly SseClientTransportOptions _options;
    private readonly McpServerConfig _serverConfig;
    private readonly HttpClient _httpClient;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// SSE transport for client endpoints. Unlike stdio it does not launch a process, but connects to an existing server.
    /// The HTTP server can be local or remote, and must support the SSE protocol.
    /// </summary>
    /// <param name="transportOptions">Configuration options for the transport.</param>
    /// <param name="serverConfig">The configuration object indicating which server to connect to.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public SseClientTransport(SseClientTransportOptions transportOptions, McpServerConfig serverConfig, ILoggerFactory? loggerFactory)
        : this(transportOptions, serverConfig, new HttpClient(), loggerFactory, true)
    {
    }

    /// <summary>
    /// SSE transport for client endpoints. Unlike stdio it does not launch a process, but connects to an existing server.
    /// The HTTP server can be local or remote, and must support the SSE protocol.
    /// </summary>
    /// <param name="transportOptions">Configuration options for the transport.</param>
    /// <param name="serverConfig">The configuration object indicating which server to connect to.</param>
    /// <param name="httpClient">The HTTP client instance used for requests.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="ownsHttpClient">True to dispose HTTP client on close connection.</param>
    public SseClientTransport(SseClientTransportOptions transportOptions, McpServerConfig serverConfig, HttpClient httpClient, ILoggerFactory? loggerFactory, bool ownsHttpClient = false)
    {
        Throw.IfNull(transportOptions);
        Throw.IfNull(serverConfig);
        Throw.IfNull(httpClient);

        _options = transportOptions;
        _serverConfig = serverConfig;
        _httpClient = httpClient;
        _loggerFactory = loggerFactory;
        _ownsHttpClient = ownsHttpClient;
    }

    /// <inheritdoc />
    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var sessionTransport = new SseClientSessionTransport(_options, _serverConfig, _httpClient, _loggerFactory);

        try
        {
            await sessionTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return sessionTransport;
        }
        catch
        {
            await sessionTransport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        return default;
    }
}