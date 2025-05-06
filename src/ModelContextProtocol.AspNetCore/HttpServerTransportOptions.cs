using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Configuration options for <see cref="M:McpEndpointRouteBuilderExtensions.MapMcp"/>.
/// which implements the Streaming HTTP transport for the Model Context Protocol.
/// See the protocol specification for details on the Streamable HTTP transport. <see href="https://modelcontextprotocol.io/specification/2025-03-26/basic/transports#streamable-http"/>
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
    public Func<HttpContext, IMcpServer, CancellationToken, Task>? RunSessionHandler { get; set; }

    /// <summary>
    /// Represents the duration of time the server will wait between any active requests before timing out an
    /// MCP session. This is checked in background every 5 seconds. A client trying to resume a session will
    /// receive a 404 status code and should restart their session. A client can keep their session open by
    /// keeping a GET request open. The default value is set to 2 hours.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// The maximum number of idle sessions to track. This is used to limit the number of sessions that can be idle at once.
    /// Past this limit, the server will log a critical error and terminate the oldest idle sessions even if they have not reached
    /// their <see cref="IdleTimeout"/> until the idle session count is below this limit. Clients that keep their session open by
    /// keeping a GET request open will not count towards this limit. The default value is set to 100,000 sessions.
    /// </summary>
    public int MaxIdleSessionCount { get; set; } = 100_000;

    /// <summary>
    /// Used for testing the <see cref="IdleTimeout"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
