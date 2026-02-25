using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents a transport mechanism for Model Context Protocol (MCP) client-to-server communication.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IClientTransport"/> interface abstracts the communication layer between MCP clients
/// and servers, allowing different transport protocols to be used interchangeably.
/// </para>
/// <para>
/// When creating an <see cref="McpClient"/>, <see cref="McpClient.CreateAsync"/> is typically used, and is
/// provided with the <see cref="IClientTransport"/> based on expected server configuration.
/// </para>
/// </remarks>
public interface IClientTransport
{
    /// <summary>
    /// Gets a transport identifier, used for logging purposes.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Asynchronously establishes a transport session with an MCP server and returns a transport for the duplex message stream.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An interface for the duplex message stream.</returns>
    /// <remarks>
    /// <para>
    /// This method is responsible for initializing the connection to the server using the specific transport
    /// mechanism implemented by the derived class. The returned <see cref="ITransport"/> interface
    /// provides methods to send and receive messages over the established connection.
    /// </para>
    /// <para>
    /// The lifetime of the returned <see cref="ITransport"/> instance is typically managed by the
    /// <see cref="McpClient"/> that uses this transport. When the client is disposed, it will dispose
    /// the transport session as well.
    /// </para>
    /// <para>
    /// This method is used by <see cref="McpClient"/> to initialize the connection.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The transport connection could not be established.</exception>
    Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default);
}
