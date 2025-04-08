using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a server that can communicate with a client using the Model Context Protocol (MCP).
/// </summary>
/// <remarks>
/// <para>
/// The IMcpServer interface provides core functionality for handling client requests,
/// managing capabilities, and enabling bidirectional communication using the MCP protocol.
/// </para>
/// <para>
/// Servers can be created using <see cref="McpServerFactory"/> or configured via dependency injection
/// with <see cref="Microsoft.Extensions.DependencyInjection.IMcpServerBuilder"/>.
/// </para>
/// <para>
/// Example usage in a basic console application:
/// <code>
/// // Create transport and options
/// var transport = new StdioTransport();
/// var options = new McpServerOptions
/// {
///     ServerCapabilities = new()
///     {
///         ToolsCollection = new()
///         {
///             McpServerTool.Create((string message) => $"Echo: {message}", 
///                 new() { Name = "Echo", Description = "Echoes a message back to the client." })
///         }
///     }
/// };
/// 
/// // Create and run the server
/// await using var server = McpServerFactory.Create(transport, options);
/// await server.RunAsync();
/// </code>
/// </para>
/// <para>
/// Example usage in a background service:
/// <code>
/// public class MyBackgroundService(IMcpServer server) : BackgroundService
/// {
///     protected override async Task ExecuteAsync(CancellationToken stoppingToken)
///     {
///         await server.RunAsync(stoppingToken);
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public interface IMcpServer : IMcpEndpoint
{
    /// <summary>
    /// Gets the capabilities supported by the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These capabilities are established during the initialization handshake and indicate
    /// which features the client supports, such as sampling, roots, and other
    /// protocol-specific functionality.
    /// </para>
    /// <para>
    /// Server implementations can check these capabilities to determine which features
    /// are available when interacting with the client.
    /// </para>
    /// <para>
    /// Example checking for sampling capability:
    /// <code>
    /// if (server.ClientCapabilities?.Sampling != null)
    /// {
    ///     // Client supports sampling capabilities
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    ClientCapabilities? ClientCapabilities { get; }

    /// <summary>
    /// Gets the version and implementation information of the connected client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property contains identification information about the client that has connected to this server,
    /// including its name and version. This information is provided by the client during initialization.
    /// </para>
    /// <para>
    /// Server implementations can use this information for logging, tracking client versions, 
    /// or implementing client-specific behaviors.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// if (server.ClientInfo != null)
    /// {
    ///     logger.LogInformation($"Connected to {server.ClientInfo.Name} version {server.ClientInfo.Version}");
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    Implementation? ClientInfo { get; }

    /// <summary>
    /// Gets the options used to construct this server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These options define the server's capabilities, protocol version, and other configuration
    /// settings that were used to initialize the server. They represent the negotiated settings
    /// that are active for the current server session.
    /// </para>
    /// <para>
    /// Example of accessing server options:
    /// <code>
    /// var protocolVersion = server.ServerOptions.ProtocolVersion;
    /// var supportsTools = server.ServerOptions.Capabilities?.Tools != null;
    /// </code>
    /// </para>
    /// </remarks>
    McpServerOptions ServerOptions { get; }

    /// <summary>
    /// Gets the service provider for the server.
    /// </summary>
    IServiceProvider? Services { get; }

    /// <summary>Gets the last logging level set by the client, or <see langword="null"/> if it's never been set.</summary>
    LoggingLevel? LoggingLevel { get; }

    /// <summary>
    /// Runs the server, listening for and handling client requests.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
