using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides configuration options for creating <see cref="McpClient"/> instances.
/// </summary>
/// <remarks>
/// These options are typically passed to <see cref="McpClient.CreateAsync"/> when creating a client.
/// They define client capabilities, protocol version, and other client-specific settings.
/// </remarks>
public sealed class McpClientOptions
{
    /// <summary>
    /// Gets or sets information about this client implementation, including its name and version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This information is sent to the server during initialization to identify the client.
    /// It's often displayed in server logs and can be used for debugging and compatibility checks.
    /// </para>
    /// <para>
    /// When not specified, information sourced from the current process is used.
    /// </para>
    /// </remarks>
    public Implementation? ClientInfo { get; set; }

    /// <summary>
    /// Gets or sets the client capabilities to advertise to the server.
    /// </summary>
    public ClientCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Gets or sets the protocol version to request from the server, using a date-based versioning scheme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The protocol version is a key part of the initialization handshake. The client and server must
    /// agree on a compatible protocol version to communicate successfully.
    /// </para>
    /// <para>
    /// If non-<see langword="null"/>, this version will be sent to the server, and the handshake
    /// will fail if the version in the server's response does not match this version.
    /// If <see langword="null"/>, the client will request the latest version supported by the server
    /// but will allow any supported version that the server advertises in its response.
    /// </para>
    /// </remarks>
    public string? ProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets a timeout for the client-server initialization handshake sequence.
    /// </summary>
    /// <value>
    /// The timeout for the client-server initialization handshake sequence. The default value is 60 seconds.
    /// </value>
    /// <remarks>
    /// <para>
    /// This timeout determines how long the client will wait for the server to respond during
    /// the initialization protocol handshake. If the server doesn't respond within this timeframe,
    /// an exception is thrown.
    /// </para>
    /// <para>
    /// Setting an appropriate timeout prevents the client from hanging indefinitely when
    /// connecting to unresponsive servers.
    /// </para>
    /// </remarks>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the container of handlers used by the client for processing protocol messages.
    /// </summary>
    public McpClientHandlers Handlers
    {
        get => field ??= new();
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the task store for managing client-side tasks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a task store is configured, the client will support task-augmented requests from the server.
    /// This allows the server to request sampling or elicitation as tasks, which the client executes
    /// asynchronously and allows the server to poll for status and results.
    /// </para>
    /// <para>
    /// If not set, task-augmented requests will not be supported, and the client will not advertise
    /// task capabilities to the server.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public IMcpTaskStore? TaskStore { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client should send task status notifications to the server.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to send task status notifications; <see langword="false"/> otherwise.
    /// The default is <see langword="true"/>.
    /// </value>
    /// <remarks>
    /// When enabled and a <see cref="TaskStore"/> is configured, the client will send optional
    /// <c>notifications/tasks/status</c> notifications to inform the server of task state changes.
    /// Servers MUST NOT rely on receiving these notifications and should continue polling via <c>tasks/get</c>.
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public bool SendTaskStatusNotifications { get; set; } = true;
}
