using ModelContextProtocol.Client;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Represents a transport mechanism for Model Context Protocol (MCP) client-to-server communication.
/// </summary>
/// <remarks>
/// The <see cref="IClientTransport"/> interface abstracts the communication layer between MCP clients
/// and servers, allowing different transport protocols to be used interchangeably. The protocol
/// provides several built-in implementations:
/// <list type="bullet">
///   <item><description><see cref="SseClientTransport"/>: HTTP-based transport using Server-Sent Events (SSE)</description></item>
///   <item><description><see cref="StdioClientTransport"/>: Process-based transport using standard input/output</description></item>
///   <item><description><see cref="StreamClientTransport"/>: Transport based on arbitrary input/output streams</description></item>
/// </list>
/// <para>
/// When creating an MCP client, you typically use the <see cref="McpClientFactory"/> to create
/// a client with the appropriate transport based on a server configuration:
/// </para>
/// <code>
/// // Create an MCP client using a stdio transport
/// await using var mcpClient = await McpClientFactory.CreateAsync(new()
/// {
///     Id = "demo-server",
///     Name = "Demo Server",
///     TransportType = TransportTypes.StdIo,
///     TransportOptions = new()
///     {
///         ["command"] = "path/to/server",
///         ["arguments"] = "--some-arg value",
///     }
/// });
/// </code>
/// </remarks>
public interface IClientTransport
{
    /// <summary>
    /// Specifies a transport identifier used for logging purposes.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Asynchronously establishes a transport session with an MCP server and returns an interface for the duplex JSON-RPC message stream.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>Returns an interface for the duplex JSON-RPC message stream.</returns>
    /// <remarks>
    /// This method is responsible for initializing the connection to the server using the specific transport 
    /// mechanism implemented by the derived class. The returned <see cref="ITransport"/> interface 
    /// provides methods to send and receive JSON-RPC messages over the established connection.
    /// 
    /// <para>Implementations should handle connection errors appropriately and clean up resources if connection fails.</para>
    /// 
    /// <para>The lifetime of the returned <see cref="ITransport"/> instance is typically managed by the 
    /// <see cref="McpClient"/> that uses this transport. When the client is disposed, it will dispose
    /// the transport session as well.</para>
    /// </remarks>
    /// <exception cref="McpTransportException">Thrown when the transport connection cannot be established.</exception>
    Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default);
}
