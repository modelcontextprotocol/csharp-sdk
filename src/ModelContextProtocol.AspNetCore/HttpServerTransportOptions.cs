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
    /// keeping a GET request open. The default value is set to 2 minutes.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Used for testing the <see cref="IdleTimeout"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
