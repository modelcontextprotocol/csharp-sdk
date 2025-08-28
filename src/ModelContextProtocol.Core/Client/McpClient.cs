using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents an instance of a Model Context Protocol (MCP) client session that connects to and communicates with an MCP server.
/// </summary>
public abstract class McpClient : McpSession
{
    /// <summary>
    /// Gets the capabilities supported by the connected server.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client is not connected.</exception>
    public abstract ServerCapabilities ServerCapabilities { get; }

    /// <summary>
    /// Gets the implementation information of the connected server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property provides identification details about the connected server, including its name and version.
    /// It is populated during the initialization handshake and is available after a successful connection.
    /// </para>
    /// <para>
    /// This information can be useful for logging, debugging, compatibility checks, and displaying server
    /// information to users.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The client is not connected.</exception>
    public abstract Implementation ServerInfo { get; }

    /// <summary>
    /// Gets any instructions describing how to use the connected server and its features.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property contains instructions provided by the server during initialization that explain
    /// how to effectively use its capabilities. These instructions can include details about available
    /// tools, expected input formats, limitations, or any other helpful information.
    /// </para>
    /// <para>
    /// This can be used by clients to improve an LLM's understanding of available tools, prompts, and resources. 
    /// It can be thought of like a "hint" to the model and may be added to a system prompt.
    /// </para>
    /// </remarks>
    public abstract string? ServerInstructions { get; }

    /// <summary>Creates an <see cref="McpClient"/>, connecting it to the specified server.</summary>
    /// <param name="clientTransport">The transport instance used to communicate with the server.</param>
    /// <param name="clientOptions">
    /// A client configuration object which specifies client capabilities and protocol version.
    /// If <see langword="null"/>, details based on the current process will be employed.
    /// </param>
    /// <param name="loggerFactory">A logger factory for creating loggers for clients.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An <see cref="McpClient"/> that's connected to the specified server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientTransport"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="clientOptions"/> is <see langword="null"/>.</exception>
    public static async Task<McpClient> CreateAsync(
        IClientTransport clientTransport,
        McpClientOptions? clientOptions = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(clientTransport);

        var transport = await clientTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var endpointName = clientTransport.Name;

        var clientSession = new McpClientImpl(transport, endpointName, clientOptions, loggerFactory);
        try
        {
            await clientSession.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await clientSession.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return clientSession;
    }
}
