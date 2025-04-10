namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides options for configuring <see cref="SseClientTransport"/> instances.
/// </summary>
public record SseClientTransportOptions
{
    /// <summary>
    /// Gets or sets the base address of the server for SSE connections.
    /// </summary>
    public required Uri Endpoint
    {
        get;
        init
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
    /// Gets a transport identifier used for logging purposes.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets or sets a timeout used to establish the initial connection to the SSE server.
    /// </summary>
    /// <remarks>
    /// This timeout controls how long the client waits for:
    /// <list type="bullet">
    ///   <item><description>The initial HTTP connection to be established with the SSE server</description></item>
    ///   <item><description>The endpoint event to be received, which indicates the message endpoint URL</description></item>
    /// </list>
    /// If the timeout expires before the connection is established, a <see cref="TimeoutException"/> will be thrown.
    /// </remarks>
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum number of reconnection attempts for the SSE connection before giving up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property controls how many times the client will attempt to reconnect to the SSE server
    /// after a connection failure occurs. If all reconnection attempts fail, a 
    /// <see cref="McpTransportException"/> with the message "Exceeded reconnect limit" will be thrown.
    /// </para>
    /// <para>
    /// Between each reconnection attempt, the client will wait for the duration specified by <see cref="ReconnectDelay"/>.
    /// </para>
    /// </remarks>
    public int MaxReconnectAttempts { get; init; } = 3;

    /// <summary>
    /// Gets or sets the delay to employ between reconnection attempts when the SSE connection fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a connection to the SSE server is lost or fails, the client will wait for this duration
    /// before attempting to reconnect. This helps prevent excessive reconnection attempts in quick succession
    /// which could overload the server or network.
    /// </para>
    /// <para>
    /// The reconnection process continues until either a successful connection is established or
    /// the maximum number of reconnection attempts (<see cref="MaxReconnectAttempts"/>) is reached.
    /// </para>
    /// </remarks>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets custom HTTP headers to include in requests to the SSE server.
    /// </summary>
    /// <remarks>
    /// Use this property to specify custom HTTP headers that should be sent with each request to the server.
    /// </remarks>
    public Dictionary<string, string>? AdditionalHeaders { get; init; }
}