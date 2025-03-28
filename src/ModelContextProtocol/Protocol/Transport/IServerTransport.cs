namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Represents a transport mechanism for MCP communication (from the server).
/// </summary>
public interface IServerTransport
{
    /// <summary>
    /// Asynchronously accepts a transport session initiated by an MCP client and returns an interface for the duplex JSON-RPC message stream.
    /// </summary>
    /// <param name="cancellationToken">Used to signal the cancellation of the asynchronous operation.</param>
    /// <returns>Returns an interface for the duplex JSON-RPC message stream.</returns>
    Task<ITransport?> AcceptAsync(CancellationToken cancellationToken = default);
}
