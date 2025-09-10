using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Configuration options for <see cref="M:McpEndpointRouteBuilderExtensions.MapMcp"/>.
/// which implements the Streaming HTTP transport for the Model Context Protocol.
/// See the protocol specification for details on the Streamable HTTP transport. <see href="https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#streamable-http"/>
/// </summary>
public class HttpServerTransportOptions
{
    /// <summary>
    /// Gets or sets an optional asynchronous callback to configure per-session <see cref="McpServerOptions"/>
    /// with access to the <see cref="HttpContext"/> of the request that initiated the session.
    /// </summary>
    public Func<HttpContext, McpServerOptions, CancellationToken, Task>? ConfigureSessionOptions { get; set; }

    /// <summary>
    /// Gets or sets an optional asynchronous callback for running new MCP sessions manually.
    /// This is useful for running logic before a sessions starts and after it completes.
    /// </summary>
    public Func<HttpContext, McpServer, CancellationToken, Task>? RunSessionHandler { get; set; }

    /// <summary>
    /// Gets or sets whether the server should run in a stateless mode that does not require all requests for a given session
    /// to arrive to the same ASP.NET Core application process.
    /// </summary>
    /// <remarks>
    /// If <see langword="true"/>, the "/sse" endpoint will be disabled, and client information will be round-tripped as part
    /// of the "MCP-Session-Id" header instead of stored in memory. Unsolicited server-to-client messages and all server-to-client
    /// requests are also unsupported, because any responses may arrive at another ASP.NET Core application process.
    /// Client sampling and roots capabilities are also disabled in stateless mode, because the server cannot make requests.
    /// Defaults to <see langword="false"/>.
    /// </remarks>
    public bool Stateless { get; set; }

    /// <summary>
    /// Gets or sets whether the server should use a single execution context for the entire session.
    /// If <see langword="false"/>, handlers like tools get called with the <see cref="ExecutionContext"/>
    /// belonging to the corresponding HTTP request which can change throughout the MCP session.
    /// If <see langword="true"/>, handlers will get called with the same <see cref="ExecutionContext"/>
    /// used to call <see cref="ConfigureSessionOptions" /> and <see cref="RunSessionHandler"/>.
    /// </summary>
    /// <remarks>
    /// Enabling a per-session <see cref="ExecutionContext"/> can be useful for setting <see cref="AsyncLocal{T}"/> variables
    /// that persist for the entire session, but it prevents you from using IHttpContextAccessor in handlers.
    /// Defaults to <see langword="false"/>.
    /// </remarks>
    public bool PerSessionExecutionContext { get; set; }

    /// <summary>
    /// Gets or sets the duration of time the server will wait between any active requests before timing out an MCP session.
    /// </summary>
    /// <remarks>
    /// This is checked in background every 5 seconds. A client trying to resume a session will receive a 404 status code
    /// and should restart their session. A client can keep their session open by keeping a GET request open.
    /// Defaults to 2 hours.
    /// </remarks>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Gets or sets maximum number of idle sessions to track in memory. This is used to limit the number of sessions that can be idle at once.
    /// </summary>
    /// <remarks>
    /// Past this limit, the server will log a critical error and terminate the oldest idle sessions even if they have not reached
    /// their <see cref="IdleTimeout"/> until the idle session count is below this limit. Clients that keep their session open by
    /// keeping a GET request open will not count towards this limit.
    /// Defaults to 10,000 sessions.
    /// </remarks>
    public int MaxIdleSessionCount { get; set; } = 10_000;

    /// <summary>
    /// Used for testing the <see cref="IdleTimeout"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
