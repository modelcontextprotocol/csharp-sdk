namespace ModelContextProtocol.Server;

/// <summary>
/// Provides options for <see cref="StreamServerTransport"/> and <see cref="StdioServerTransport"/>.
/// </summary>
public class StreamServerTransportOptions
{
    private long? _maxMessageSize;

    /// <summary>
    /// Gets or sets the maximum size, in bytes, of a single incoming JSON-RPC message.
    /// </summary>
    /// <value>
    /// The maximum number of UTF-8 bytes in one newline-delimited message, not counting the newline,
    /// or <see langword="null"/> for no limit. The default is <see langword="null"/>.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    /// <remarks>
    /// <para>
    /// Without a limit, the transport buffers each incoming line until its newline arrives, however
    /// long the line is. With a limit, the transport stops buffering a line as soon as it exceeds the
    /// limit and discards the rest of it up to the next newline, so the memory used to read it stays
    /// bounded by roughly the limit.
    /// </para>
    /// <para>
    /// The transport scans a discarded message for its top-level <c>id</c> in constant memory. If the
    /// message is a request, the transport replies with a JSON-RPC <see cref="McpErrorCode.InvalidRequest"/>
    /// error for that id. If it is a response to a request that this server sent, such as a large
    /// sampling result, the pending request fails locally with an <see cref="McpProtocolException"/>. A
    /// notification, or a message whose id cannot be found, is dropped and logged.
    /// </para>
    /// </remarks>
    public long? MaxMessageSize
    {
        get => _maxMessageSize;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum message size must be positive.");
            }

            _maxMessageSize = value;
        }
    }
}
