using Microsoft.Extensions.Logging;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides an <see cref="IClientTransport"/> over HTTP using the Server-Sent Events (SSE) or Streamable HTTP protocol.
/// </summary>
/// <remarks>
/// This transport connects to an MCP server over HTTP using SSE or Streamable HTTP,
/// allowing for real-time server-to-client communication with a standard HTTP requests.
/// Unlike the <see cref="StdioClientTransport"/>, this transport connects to an existing server
/// rather than launching a new process.
/// </remarks>
public sealed class SseClientTransport : IClientTransport, IAsyncDisposable
{
    private readonly SseClientTransportOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientTransport"/> class.
    /// </summary>
    /// <param name="transportOptions">Configuration options for the transport.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers used for diagnostic output during transport operations.</param>
    public SseClientTransport(SseClientTransportOptions transportOptions, ILoggerFactory? loggerFactory = null)
        : this(transportOptions, new HttpClient(), loggerFactory, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientTransport"/> class with a provided HTTP client.
    /// </summary>
    /// <param name="transportOptions">Configuration options for the transport.</param>
    /// <param name="httpClient">The HTTP client instance used for requests.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers used for diagnostic output during transport operations.</param>
    /// <param name="ownsHttpClient">
    /// <see langword="true"/> to dispose of <paramref name="httpClient"/> when the transport is disposed;
    /// <see langword="false"/> if the caller is retaining ownership of the <paramref name="httpClient"/>'s lifetime.
    /// </param>
    public SseClientTransport(SseClientTransportOptions transportOptions, HttpClient httpClient, ILoggerFactory? loggerFactory = null, bool ownsHttpClient = false)
    {
        Throw.IfNull(transportOptions);
        Throw.IfNull(httpClient);

        _options = transportOptions;
        _httpClient = httpClient;
        _loggerFactory = loggerFactory;
        _ownsHttpClient = ownsHttpClient;
        Name = transportOptions.Name ?? transportOptions.Endpoint.ToString();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientTransport"/> class with authentication support.
    /// </summary>
    /// <param name="transportOptions">Configuration options for the transport.</param>
    /// <param name="credentialProvider">The authorization provider to use for authentication.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers used for diagnostic output during transport operations.</param>
    /// <param name="baseMessageHandler">Optional. The base message handler to use under the authorization handler.
    /// If null, a new <see cref="HttpClientHandler"/> will be used. This allows for custom HTTP client pipelines (e.g., from HttpClientFactory)
    /// to be used in conjunction with the token-based authentication provided by <paramref name="credentialProvider"/>.</param>
    public SseClientTransport(SseClientTransportOptions transportOptions, IMcpCredentialProvider credentialProvider, ILoggerFactory? loggerFactory = null, HttpMessageHandler? baseMessageHandler = null)
    {
        Throw.IfNull(transportOptions);
        Throw.IfNull(credentialProvider);

        _options = transportOptions;
        _loggerFactory = loggerFactory;
        Name = transportOptions.Name ?? transportOptions.Endpoint.ToString();

        var authHandler = new AuthorizationDelegatingHandler(credentialProvider)
        {
            InnerHandler = baseMessageHandler ?? new HttpClientHandler()
        };

        _httpClient = new HttpClient(authHandler);
        _ownsHttpClient = true;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return _options.TransportMode switch
        {
            HttpTransportMode.AutoDetect => new AutoDetectingClientSessionTransport(_options, _httpClient, _loggerFactory, Name),
            HttpTransportMode.StreamableHttp => new StreamableHttpClientSessionTransport(Name, _options, _httpClient, messageChannel: null, _loggerFactory),
            HttpTransportMode.Sse => await ConnectSseTransportAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported transport mode: {_options.TransportMode}"),
        };
    }

    private async Task<ITransport> ConnectSseTransportAsync(CancellationToken cancellationToken)
    {
        var sessionTransport = new SseClientSessionTransport(Name, _options, _httpClient, messageChannel: null, _loggerFactory);

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