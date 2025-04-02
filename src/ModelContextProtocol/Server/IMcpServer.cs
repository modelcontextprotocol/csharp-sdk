using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a server that can communicate with a client using the MCP protocol.
/// </summary>
public interface IMcpServer : IMcpEndpoint
{
    /// <summary>
    /// Gets the capabilities supported by the client.
    /// </summary>
    ClientCapabilities? ClientCapabilities { get; }

    /// <summary>
    /// Gets the version and implementation information of the client.
    /// </summary>
    Implementation? ClientInfo { get; }

    /// <summary>Gets the options used to construct this server.</summary>
    McpServerOptions ServerOptions { get; }

    /// <summary>
    /// Gets the service provider for the server.
    /// </summary>
    IServiceProvider? Services { get; }

    /// <summary>
    /// Runs the server, listening for and handling client requests.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
