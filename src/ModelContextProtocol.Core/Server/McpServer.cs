using System.Diagnostics.CodeAnalysis;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an instance of a Model Context Protocol (MCP) server that connects to and communicates with an MCP client.
/// </summary>
public abstract partial class McpServer : McpSession
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpServer"/> class.
    /// </summary>
    [Experimental(Experimentals.Subclassing_DiagnosticId, UrlFormat = Experimentals.Subclassing_Url)]
    protected McpServer()
    {
    }

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
    /// </remarks>
    public abstract ClientCapabilities? ClientCapabilities { get; }

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
    /// </remarks>
    public abstract Implementation? ClientInfo { get; }

    /// <summary>
    /// Gets the options used to construct this server.
    /// </summary>
    /// <remarks>
    /// These options define the server's capabilities, protocol version, and other configuration
    /// settings that were used to initialize the server.
    /// </remarks>
    public abstract McpServerOptions ServerOptions { get; }

    /// <summary>
    /// Gets the service provider for the server.
    /// </summary>
    public abstract IServiceProvider? Services { get; }

    /// <summary>Gets the last logging level set by the client, or <see langword="null"/> if it's never been set.</summary>
    public abstract LoggingLevel? LoggingLevel { get; }

    /// <summary>
    /// Runs the server, listening for and handling client requests.
    /// </summary>
    public abstract Task RunAsync(CancellationToken cancellationToken = default);
}
