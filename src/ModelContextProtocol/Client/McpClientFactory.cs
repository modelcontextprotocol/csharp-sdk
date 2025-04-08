using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides factory methods for creating MCP clients.
/// </summary>
/// <remarks>
/// <para>
/// This factory class is the primary way to instantiate <see cref="IMcpClient"/> instances
/// that connect to Model Context Protocol (MCP) servers. It handles the creation and initialization
/// of appropriate transport implementations based on the provided server configuration.
/// </para>
/// <para>
/// The factory supports different transport types including StdIo and SSE (Server-Sent Events),
/// and handles platform-specific considerations like shell command formatting.
/// </para>
/// </remarks>
public static class McpClientFactory
{
    /// <summary>Creates an <see cref="IMcpClient"/>, connecting it to the specified server.</summary>
    /// <param name="clientTransport">The transport instance used to communicate with the server.</param>
    /// <param name="clientOptions">
    /// A client configuration object which specifies client capabilities and protocol version.
    /// If <see langword="null"/>, details based on the current process will be employed.
    /// </param>
    /// <param name="loggerFactory">A logger factory for creating loggers for clients.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An <see cref="IMcpClient"/> that's connected to the specified server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientTransport"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="clientOptions"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="createTransportFunc"/> returns an invalid transport.</exception>
    /// <example>
    /// <code>
    /// // Connect to an MCP server via StdIo transport
    /// await using var mcpClient = await McpClientFactory.CreateAsync(
    ///     new()
    ///     {
    ///         Id = "demo-server",
    ///         Name = "Demo Server",
    ///         TransportType = TransportTypes.StdIo,
    ///         TransportOptions = new()
    ///         {
    ///             ["command"] = "npx",
    ///             ["arguments"] = "-y @modelcontextprotocol/server-everything",
    ///         }
    ///     });
    ///     
    /// // List available tools from the server
    /// var tools = await mcpClient.ListToolsAsync();
    /// foreach (var tool in tools)
    /// {
    ///     Console.WriteLine($"Connected to server with tool: {tool.Name}");
    /// }
    /// </code>
    /// </example>
    public static async Task<IMcpClient> CreateAsync(
        IClientTransport clientTransport,
        McpClientOptions? clientOptions = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(clientTransport);

        string endpointName = clientTransport.Name;
        var logger = loggerFactory?.CreateLogger(typeof(McpClientFactory)) ?? NullLogger.Instance;
        logger.CreatingClient(endpointName);

        McpClient client = new(clientTransport, clientOptions, loggerFactory);
        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            logger.ClientCreated(endpointName);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}