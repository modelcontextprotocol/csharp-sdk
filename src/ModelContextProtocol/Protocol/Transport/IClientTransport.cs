namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Represents a transport mechanism for MCP communication (from the client).
/// </summary>
public interface IClientTransport
{
    /// <summary>
    /// Asynchronously establishes a transport session with an MCP server and returns an interface for the duplex JSON-RPC message stream.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Returns an interface for the duplex JSON-RPC message stream.</returns>
    Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default);
}
