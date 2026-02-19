using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Provides a method to create a <see cref="RequestDelegate"/> that handles MCP Streamable HTTP
/// transport requests for an ASP.NET Core server.
/// </summary>
/// <remarks>
/// <para>
/// This factory creates a <see cref="RequestDelegate"/> that routes MCP requests
/// based on the HTTP method (POST, GET, DELETE) of the incoming request.
/// The required services must be registered by calling <c>WithHttpTransport()</c>
/// during application startup.
/// </para>
/// <para>
/// This is useful for integrating MCP into applications that use traditional MVC controllers
/// or other request-handling patterns instead of minimal APIs:
/// </para>
/// <code>
/// [ApiController]
/// [Route("mcp")]
/// public class McpController : ControllerBase
/// {
///     private static readonly RequestDelegate _mcpHandler = McpRequestDelegateFactory.Create();
///
///     [HttpPost]
///     [HttpGet]
///     [HttpDelete]
///     public Task Handle() =&gt; _mcpHandler(HttpContext);
/// }
/// </code>
/// </remarks>
public static class McpRequestDelegateFactory
{
    /// <summary>
    /// Creates a <see cref="RequestDelegate"/> that handles MCP Streamable HTTP transport requests.
    /// </summary>
    /// <returns>
    /// A <see cref="RequestDelegate"/> that routes incoming requests to the appropriate MCP handler
    /// based on the HTTP method. POST requests handle JSON-RPC messages, GET requests handle
    /// SSE streams for server-to-client messages, and DELETE requests terminate sessions.
    /// Unsupported HTTP methods receive a 405 Method Not Allowed response.
    /// </returns>
    /// <remarks>
    /// The returned delegate resolves the internal MCP handler from <see cref="HttpContext.RequestServices"/>
    /// on each invocation. The required services are registered by
    /// calling <c>WithHttpTransport()</c> during application startup.
    /// </remarks>
    public static RequestDelegate Create()
    {
        return context =>
        {
            var handler = context.RequestServices.GetRequiredService<StreamableHttpHandler>();

            return context.Request.Method switch
            {
                "POST" => handler.HandlePostRequestAsync(context),
                "GET" => handler.HandleGetRequestAsync(context),
                "DELETE" => handler.HandleDeleteRequestAsync(context),
                _ => HandleMethodNotAllowedAsync(context),
            };
        };
    }

    private static Task HandleMethodNotAllowedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return Task.CompletedTask;
    }
}
