using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Represents configuration options for <see cref="M:McpEndpointRouteBuilderExtensions.MapMcp"/>,
/// which implements the Streamable HTTP transport for the Model Context Protocol.
/// See the protocol specification for details on the Streamable HTTP transport. <see href="https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http"/>
/// </summary>
/// <remarks>
/// For details on the Streamable HTTP transport, see the <see href="https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http">protocol specification</see>.
/// </remarks>
public class HttpServerTransportOptions
{
    /// <summary>
    /// Gets or sets an optional asynchronous callback to configure per-session <see cref="McpServerOptions"/>
    /// with access to the <see cref="HttpContext"/> of the request that initiated the session.
    /// </summary>
    /// <remarks>
    /// In stateful mode (the default), this callback is invoked once per session when the client sends the
    /// <c>initialize</c> request. In <see cref="Stateless"/> mode, it is invoked on <b>every HTTP request</b>
    /// because each request creates a fresh server context.
    /// </remarks>
    public Func<HttpContext, McpServerOptions, CancellationToken, Task>? ConfigureSessionOptions { get; set; }

    /// <summary>
    /// Gets or sets an optional asynchronous callback for running new MCP sessions manually.
    /// </summary>
    /// <remarks>
    /// This callback is useful for running logic before a session starts and after it completes.
    /// <para>
    /// The <see cref="HttpContext"/> parameter comes from the request that initiated the session (e.g., the
    /// initialize request) and may not be usable after <see cref="McpServer.RunAsync"/> starts, since that
    /// request will have already completed.
    /// </para>
    /// <para>
    /// Consider using <see cref="ConfigureSessionOptions"/> instead, which provides access to the
    /// <see cref="HttpContext"/> of the initializing request with fewer known issues.
    /// </para>
    /// <para>
    /// This API is experimental and may be removed or change signatures in a future release.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.Experimental(Experimentals.RunSessionHandler_DiagnosticId, UrlFormat = Experimentals.RunSessionHandler_Url)]
    public Func<HttpContext, McpServer, CancellationToken, Task>? RunSessionHandler { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the server runs in a stateless mode that doesn't track state between requests,
    /// allowing for load balancing without session affinity.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the server runs in a stateless mode; <see langword="false"/> if the server tracks state between requests. The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// If <see langword="true"/>, <see cref="McpSession.SessionId"/> will be null, and the "MCP-Session-Id" header will not be used,
    /// the <see cref="RunSessionHandler"/> will be called once for each request, and the "/sse" endpoint will be disabled.
    /// Unsolicited server-to-client messages and all server-to-client requests are also unsupported, because any responses
    /// might arrive at another ASP.NET Core application process.
    /// Client sampling, elicitation, and roots capabilities are also disabled in stateless mode, because the server cannot make requests.
    /// </remarks>
    public bool Stateless { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the server maps legacy SSE endpoints (<c>/sse</c> and <c>/message</c>)
    /// for backward compatibility with clients that do not support the Streamable HTTP transport.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to map the legacy SSE endpoints; <see langword="false"/> to disable them. The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// The legacy SSE transport separates request and response channels: clients POST JSON-RPC messages
    /// to <c>/message</c> and receive responses through a long-lived GET SSE stream on <c>/sse</c>.
    /// Because the POST endpoint returns <c>202 Accepted</c> immediately, there is no HTTP-level
    /// backpressure on handler concurrency — unlike Streamable HTTP, where each POST is held open
    /// until the handler responds.
    /// </para>
    /// <para>
    /// Use Streamable HTTP instead whenever possible. If you must support legacy SSE clients,
    /// enable this property only for completely trusted clients in isolated processes, and apply
    /// HTTP rate-limiting middleware and reverse proxy limits to compensate for the lack of
    /// built-in backpressure.
    /// </para>
    /// <para>
    /// Setting this to <see langword="true"/> while <see cref="Stateless"/> is also <see langword="true"/>
    /// throws an <see cref="InvalidOperationException"/> at startup, because SSE requires in-memory session state.
    /// </para>
    /// <para>
    /// This property can also be enabled via the <c>ModelContextProtocol.AspNetCore.EnableLegacySse</c>
    /// <see cref="AppContext"/> switch.
    /// </para>
    /// </remarks>
    [Obsolete(Obsoletions.EnableLegacySse_Message, DiagnosticId = Obsoletions.EnableLegacySse_DiagnosticId, UrlFormat = Obsoletions.EnableLegacySse_Url)]
    public bool EnableLegacySse { get; set; }

    /// <summary>
    /// Gets or sets the event store for resumability support.
    /// When set, events are stored and can be replayed when clients reconnect with a Last-Event-ID header.
    /// </summary>
    /// <remarks>
    /// When configured, the server will:
    /// <list type="bullet">
    /// <item><description>Generate unique event IDs for each SSE message</description></item>
    /// <item><description>Store events for later replay</description></item>
    /// <item><description>Replay missed events when a client reconnects with a Last-Event-ID header</description></item>
    /// <item><description>Send priming events to establish resumability before any actual messages</description></item>
    /// </list>
    /// <para>
    /// This can be set directly, or an <see cref="ISseEventStreamStore"/> can be registered in DI.
    /// If this property is not set, the server will attempt to resolve an <see cref="ISseEventStreamStore"/> from DI.
    /// </para>
    /// </remarks>
    public ISseEventStreamStore? EventStreamStore { get; set; }

    /// <summary>
    /// Gets or sets the session migration handler for cross-instance session migration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When configured, the server will support session migration between instances.
    /// If a request arrives with a session ID that is not found locally, the handler
    /// is consulted to determine if the session can be migrated from another instance.
    /// </para>
    /// <para>
    /// This can be set directly, or an <see cref="ISessionMigrationHandler"/> can be registered in DI.
    /// If this property is not set, the server will attempt to resolve an <see cref="ISessionMigrationHandler"/> from DI.
    /// </para>
    /// </remarks>
    public ISessionMigrationHandler? SessionMigrationHandler { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the server uses a single execution context for the entire session.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the server uses a single execution context for the entire session; otherwise, <see langword="false"/>. The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// If <see langword="false"/>, handlers like tools get called with the <see cref="ExecutionContext"/>
    /// belonging to the corresponding HTTP request, which can change throughout the MCP session.
    /// If <see langword="true"/>, handlers will get called with the same <see cref="ExecutionContext"/>
    /// used to call <see cref="ConfigureSessionOptions" /> and <see cref="RunSessionHandler"/>.
    /// Enabling a per-session <see cref="ExecutionContext"/> can be useful for setting <see cref="AsyncLocal{T}"/> variables
    /// that persist for the entire session, but it prevents you from using IHttpContextAccessor in handlers.
    /// </remarks>
    public bool PerSessionExecutionContext { get; set; }

    /// <summary>
    /// Gets or sets the duration of time the server will wait between any active requests before timing out an MCP session.
    /// </summary>
    /// <value>
    /// The amount of time the server waits between any active requests before timing out an MCP session. The default is 2 hours.
    /// </value>
    /// <remarks>
    /// <para>
    /// This value is checked in the background every 5 seconds. A client trying to resume a session will receive a 404 status code
    /// and should restart their session. A client can keep their session open by keeping a GET request open.
    /// </para>
    /// <para>
    /// Legacy SSE sessions (when <see cref="EnableLegacySse"/> is enabled) are not subject to this timeout — their lifetime is
    /// tied to the open GET <c>/sse</c> request, and they are removed immediately when the client disconnects.
    /// </para>
    /// </remarks>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Gets or sets the maximum number of idle sessions to track in memory. This value is used to limit the number of sessions that can be idle at once.
    /// </summary>
    /// <value>
    /// The maximum number of idle sessions to track in memory. The default is 10,000 sessions.
    /// </value>
    /// <remarks>
    /// <para>
    /// Past this limit, the server logs a critical error and terminates the oldest idle sessions, even if they have not reached
    /// their <see cref="IdleTimeout"/>, until the idle session count is below this limit. Sessions with any active HTTP request
    /// are not considered idle and don't count towards this limit.
    /// </para>
    /// <para>
    /// Legacy SSE sessions (when <see cref="EnableLegacySse"/> is enabled) are never considered idle because their lifetime is
    /// tied to the open GET <c>/sse</c> request. They are not subject to <see cref="IdleTimeout"/> or this limit — they exist
    /// exactly as long as the SSE connection is open.
    /// </para>
    /// </remarks>
    public int MaxIdleSessionCount { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the time provider that's used for testing the <see cref="IdleTimeout"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
