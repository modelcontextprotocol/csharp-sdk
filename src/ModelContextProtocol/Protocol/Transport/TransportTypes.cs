namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// List all transport types
/// </summary>
public static class TransportTypes
{
    /// <summary>
    /// The name of the Standard IO transport.
    /// </summary>
    public const string StdIo = "stdio";

    /// <summary>
    /// The name of the ServerSideEvents transport.
    /// </summary>
    public const string Sse = "sse";

    /// <summary>
    /// The name of the InMemory transport.
    /// </summary>
    public const string InMemory = "inmemory";
}
