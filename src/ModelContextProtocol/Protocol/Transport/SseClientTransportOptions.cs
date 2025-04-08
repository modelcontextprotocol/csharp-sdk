namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Options for configuring the SSE transport.
/// </summary>
public record SseClientTransportOptions
{
    /// <summary>
    /// The base address of the server for SSE connections.
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
    /// Specifies a transport identifier used for logging purposes.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Timeout for initial connection and endpoint event.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of reconnection attempts for SSE connection.
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 3;

    /// <summary>
    /// Delay between reconnection attempts.
    /// </summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Headers to include in HTTP requests.
    /// </summary>
    public Dictionary<string, string>? AdditionalHeaders { get; init; }
}