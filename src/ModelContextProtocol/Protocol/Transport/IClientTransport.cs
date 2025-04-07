namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Represents a transport mechanism for MCP communication (from the client).
/// </summary>
public interface IClientTransport
{
    /// <summary>
    /// Gets a description of the endpoint the client is connecting to.
    /// </summary>
    string EndpointName { get; }

    /// <summary>
    /// Asynchronously establishes a transport session with an MCP server and returns an interface for the duplex JSON-RPC message stream.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>Returns an interface for the duplex JSON-RPC message stream.</returns>
    Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default);
}
