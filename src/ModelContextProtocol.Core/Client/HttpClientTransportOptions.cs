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
    /// Gets or sets the transport mode to use for the connection. Defaults to <see cref="HttpTransportMode.AutoDetect"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set to <see cref="HttpTransportMode.AutoDetect"/> (the default), the client will first attempt to use
    /// Streamable HTTP transport and automatically fall back to SSE transport if the server doesn't support it.
    /// </para>
    /// <para>
    /// <see href="https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#streamable-http">Streamable HTTP transport specification</see>.
    /// <see href="https://modelcontextprotocol.io/specification/2024-11-05/basic/transports#http-with-sse">HTTP with SSE transport specification</see>.
    /// </para>
    /// </remarks>
    public HttpTransportMode TransportMode { get; set; } = HttpTransportMode.AutoDetect;

    /// <summary>
    /// Gets or sets a transport identifier used for logging purposes.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a timeout used to establish the initial connection to the SSE server. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// This timeout controls how long the client waits for:
    /// <list type="bullet">
    ///   <item><description>The initial HTTP connection to be established with the SSE server</description></item>
    ///   <item><description>The endpoint event to be received, which indicates the message endpoint URL</description></item>
    /// </list>
    /// If the timeout expires before the connection is established, a <see cref="TimeoutException"/> will be thrown.
    /// </remarks>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets custom HTTP headers to include in requests to the SSE server.
    /// </summary>
    /// <remarks>
    /// Use this property to specify custom HTTP headers that should be sent with each request to the server.
    /// </remarks>
    public IDictionary<string, string>? AdditionalHeaders { get; set; }

    /// <summary>
    /// Gets sor sets the authorization provider to use for authentication.
    /// </summary>
    public ClientOAuthOptions? OAuth { get; set; }
}