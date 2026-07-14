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
    /// On protocol revisions that use the <c>initialize</c> handshake (<c>2025-11-25</c> and earlier), these
    /// capabilities are established once during initialization and are session-scoped: they are available both
    /// on the root <see cref="McpServer"/> and on the server exposed to request handlers.
    /// </para>
    /// <para>
    /// On the <c>2026-07-28</c> revision and later (SEP-2575) there is no <c>initialize</c> handshake; the client
    /// declares its capabilities per-request in <c>_meta</c>, and the server MUST NOT infer them from previous
    /// requests. In that mode this property is only meaningful on the request-scoped server accessed via
    /// the <c>Server</c> property of the <see cref="RequestContext{TParams}"/> passed to a handler; on the
    /// root <see cref="McpServer"/> (for example one constructed manually over a
    /// <see cref="System.IO.Stream"/>) it is <see langword="null"/>.
    /// It is also <see langword="null"/> in stateless transport mode, where server-to-client requests are
    /// unsupported.
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
    /// including its name and version.
    /// </para>
    /// <para>
    /// On protocol revisions that use the <c>initialize</c> handshake (<c>2025-11-25</c> and earlier) this
    /// information is provided once during initialization and is session-scoped. On the <c>2026-07-28</c>
    /// revision and later it is carried per-request in <c>_meta</c>, so read it from the request-scoped server
    /// accessed via the <c>Server</c> property of the <see cref="RequestContext{TParams}"/> passed to a handler
    /// rather than from the root <see cref="McpServer"/>.
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
    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public abstract LoggingLevel? LoggingLevel { get; }

    /// <summary>
    /// Gets a value indicating whether the connected client supports Multi Round-Trip Requests (MRTR).
    /// </summary>
    /// <remarks>
    /// <para>
    /// When this property returns <see langword="true"/>, tool handlers can throw
    /// <see cref="Protocol.InputRequiredException"/> to return an <see cref="Protocol.InputRequiredResult"/>
    /// with <see cref="Protocol.InputRequiredResult.InputRequests"/> and/or
    /// <see cref="Protocol.InputRequiredResult.RequestState"/> to the client.
    /// </para>
    /// <para>
    /// When this property returns <see langword="false"/>, tool handlers should provide a fallback
    /// experience (for example, returning a text message explaining that the client does not support
    /// the required feature) instead of throwing <see cref="Protocol.InputRequiredException"/>.
    /// </para>
    /// </remarks>
    public virtual bool IsMrtrSupported => false;

    /// <summary>
    /// Runs the server, listening for and handling client requests.
    /// </summary>
    public abstract Task RunAsync(CancellationToken cancellationToken = default);
}
