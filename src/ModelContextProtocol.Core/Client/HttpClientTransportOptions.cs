using ModelContextProtocol.Authentication;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides options for configuring <see cref="HttpClientTransport"/> instances.
/// </summary>
public sealed class HttpClientTransportOptions
{
    /// <summary>
    /// Gets or sets the base address of the server for SSE connections.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value is not an absolute URI, or does not use the HTTP or HTTPS scheme.</exception>
    public required Uri Endpoint
    {
        get;
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value), "Endpoint cannot be null.");
            }
            if (!value.IsAbsoluteUri)
            {
                throw new ArgumentException("Endpoint must be an absolute URI.", nameof(value));
            }
            if (value.Scheme != Uri.UriSchemeHttp && value.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("Endpoint must use HTTP or HTTPS scheme.", nameof(value));
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the transport mode to use for the connection.
    /// </summary>
    /// <value>
    /// The transport mode to use for the connection. The default is <see cref="HttpTransportMode.AutoDetect"/>.
    /// </value>
    /// <remarks>
    /// When set to <see cref="HttpTransportMode.AutoDetect"/> (the default), the client will first attempt to use
    /// Streamable HTTP transport and automatically fall back to SSE transport if the server doesn't support it.
    /// </remarks>
    /// <seealso href="https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http">Streamable HTTP transport specification</seealso>.
    /// <seealso href="https://modelcontextprotocol.io/specification/2024-11-05/basic/transports#http-with-sse">HTTP with SSE transport specification</seealso>.
    public HttpTransportMode TransportMode { get; set; } = HttpTransportMode.AutoDetect;

    /// <summary>
    /// Gets or sets a transport identifier used for logging purposes.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a timeout used to establish the initial connection to the SSE server.
    /// </summary>
    /// <value>
    /// The timeout used to establish the initial connection to the SSE server. The default is 30 seconds.
    /// </value>
    /// <remarks>
    /// This timeout controls how long the client waits for:
    /// <list type="bullet">
    ///   <item><description>The initial HTTP connection to be established with the SSE server.</description></item>
    ///   <item><description>The endpoint event to be received, which indicates the message endpoint URL.</description></item>
    /// </list>
    /// If the timeout expires before the connection is established, a <see cref="TimeoutException"/> is thrown.
    /// </remarks>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets custom HTTP headers to include in requests to the SSE server.
    /// </summary>
    /// <remarks>
    /// Use this property to specify custom HTTP headers that should be sent with each request to the server.
    /// </remarks>
    public IDictionary<string, string>? AdditionalHeaders { get; set; }

    /// <summary>
    /// Gets or sets a session identifier that should be reused when connecting to a Streamable HTTP server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When non-<see langword="null"/>, the transport assumes the server already created the session and will include the
    /// specified session identifier in every HTTP request. This allows reconnecting to an existing session created in a
    /// previous process. This option is only supported by the Streamable HTTP transport mode.
    /// </para>
    /// <para>
    /// Clients should pair this with
    /// <see cref="McpClient.ResumeSessionAsync(IClientTransport, ResumeClientSessionOptions, McpClientOptions?, Microsoft.Extensions.Logging.ILoggerFactory?, CancellationToken)"/>
    /// to skip the initialization handshake when rehydrating a previously negotiated session.
    /// </para>
    /// </remarks>
    public string? KnownSessionId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this transport endpoint is responsible for ending the session on dispose.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/> (default), the transport sends a DELETE request that informs the server the session is
    /// complete. Set this to <see langword="false"/> when creating a transport used solely to bootstrap session information
    /// that will later be resumed elsewhere.
    /// </remarks>
    public bool OwnsSession { get; set; } = true;

    /// <summary>
    /// Gets or sets the authorization provider to use for authentication.
    /// </summary>
    public ClientOAuthOptions? OAuth { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of consecutive reconnection attempts when an SSE stream is disconnected.
    /// </summary>
    /// <value>
    /// The maximum number of reconnection attempts. The default is 5.
    /// </value>
    /// <remarks>
    /// When an SSE stream is disconnected (e.g., due to a network issue), the client will attempt to
    /// reconnect using the Last-Event-ID header to resume from where it left off. This property controls
    /// how many consecutive reconnection attempts are made before giving up. The counter resets to zero
    /// on each successful stream read, so this value only limits consecutive failures.
    /// </remarks>
    public int MaxReconnectionAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the default interval at which the client attempts reconnection after an SSE stream is disconnected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default value is 1 second.
    /// </para>
    /// <para>
    /// If the server sends a message specifying a different reconnection interval, that new value will be used for all
    /// subsequent reconnection attempts for that stream.
    /// </para>
    /// </remarks>
    public TimeSpan DefaultReconnectionInterval { get; set; } = TimeSpan.FromSeconds(1);
}
