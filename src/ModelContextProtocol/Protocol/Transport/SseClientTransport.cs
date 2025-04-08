using Microsoft.Extensions.Logging;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides a client MCP transport implemented using Server-Sent Events (SSE) over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// This transport connects to an MCP server over HTTP using the Server-Sent Events protocol,
/// allowing for real-time server-to-client communication with a standard HTTP request.
/// Unlike the <see cref="StdioClientTransport"/>, this transport connects to an existing server
/// rather than launching a new process.
/// </para>
/// <para>
/// SSE is a unidirectional communication protocol where the server can push messages to the client
/// in real-time. For bidirectional communication, this transport sends client messages via regular
/// HTTP POST requests to a configured endpoint, while receiving server messages through the SSE connection.
/// </para>
/// <para>
/// This transport is ideal for web-based scenarios where the server is accessible via HTTP/HTTPS
/// and provides a standard way to integrate with MCP servers running in cloud environments or behind
/// API gateways.
/// </para>
/// <example>
/// <code>
/// // Creating an MCP client with SSE transport
/// await using var mcpClient = await McpClientFactory.CreateAsync(new McpServerConfig
/// {
///     Id = "server-id",
///     Name = "Server Name",
///     TransportType = TransportTypes.Sse,
///     TransportOptions = new()
///     {
///         ["baseUrl"] = "https://api.example.com/mcp",
///         ["headers"] = new Dictionary&lt;string, string&gt;
///         {
///             ["Authorization"] = "Bearer token123"
///         }
///     }
/// });
/// </code>
/// </example>
/// </remarks>
public sealed class SseClientTransport : IClientTransport, IAsyncDisposable
{
    private readonly SseClientTransportOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientTransport"/> class with a new HTTP client.
    /// </summary>
    /// <param name="transportOptions">Configuration options for the transport.</param>
    /// <param name="serverConfig">The configuration object indicating which server to connect to.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers. Used for diagnostic output during transport operations.
    /// If null, a NullLogger will be used that doesn't produce any output.</param>
    public SseClientTransport(SseClientTransportOptions transportOptions, ILoggerFactory? loggerFactory = null)
        : this(transportOptions, new HttpClient(), loggerFactory, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientTransport"/> class with a provided HTTP client.
    /// </summary>
    /// <param name="transportOptions">Configuration options for the transport.</param>
    /// <param name="httpClient">The HTTP client instance used for requests.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers. Used for diagnostic output during transport operations.
    /// If null, a NullLogger will be used that doesn't produce any output.</param>
    /// <param name="ownsHttpClient">True to dispose HTTP client when the transport is disposed; false to leave it for the caller to manage.</param>
    /// <remarks>
    /// This constructor allows providing an external HTTP client, which can be useful for advanced scenarios
    /// where you need to configure the HTTP client with custom handlers, base address, or default headers.
    /// </remarks>\
    public SseClientTransport(SseClientTransportOptions transportOptions, McpServerConfig serverConfig, HttpClient httpClient, ILoggerFactory? loggerFactory, bool ownsHttpClient = false)
    {
        Throw.IfNull(transportOptions);
        Throw.IfNull(httpClient);

        _options = transportOptions;
        _httpClient = httpClient;
        _loggerFactory = loggerFactory;
        _ownsHttpClient = ownsHttpClient;
        Name = transportOptions.Name ?? transportOptions.Endpoint.ToString();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var sessionTransport = new SseClientSessionTransport(_options, _httpClient, _loggerFactory, Name);

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
    /// <summary>
    /// Asynchronously releases all resources used by the SSE client transport.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    /// <remarks>
    /// This method will dispose the HttpClient if the transport owns it 
    /// (based on the <see cref="_ownsHttpClient"/> flag).
    /// After disposal, the transport can no longer be used to establish new connections.
    /// Any active connections created by this transport should be disposed separately.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        return default;
    }
}