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
    /// When true, the server includes an absolute URL
    /// (e.g. "https://api.example.com/message?sessionId=…") for the message-posting
    /// endpoint in the initial init SSE event.  
    /// When false (default), a relative URL ("message?sessionId=…") is sent.
    /// </summary>
    public bool SendAbsoluteMessageUrl { get; set; } = false;
}
