using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to add MCP endpoints.
/// </summary>
public static class McpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Sets up endpoints for handling MCP Streamable HTTP transport.
    /// See <see href="https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#streamable-http">the 2025-06-18 protocol specification</see> for details about the Streamable HTTP transport.
    /// Also maps legacy SSE endpoints for backward compatibility at the path "/sse" and "/message". <see href="https://modelcontextprotocol.io/specification/2024-11-05/basic/transports#http-with-sse">the 2024-11-05 protocol specification</see> for details about the HTTP with SSE transport.
    /// </summary>
    /// <param name="endpoints">The web application to attach MCP HTTP endpoints.</param>
    /// <param name="pattern">The route pattern prefix to map to.</param>
    /// <returns>Returns a builder for configuring additional endpoint conventions like authorization policies.</returns>
    public static IEndpointConventionBuilder MapMcp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
    {
        var streamableHttpHandler = endpoints.ServiceProvider.GetService<StreamableHttpHandler>() ??
            throw new InvalidOperationException("You must call WithHttpTransport(). Unable to find required services. Call builder.Services.AddMcpServer().WithHttpTransport() in application startup code.");

        var mcpGroup = endpoints.MapGroup(pattern);
        var streamableHttpGroup = mcpGroup.MapGroup("")
            .WithDisplayName(b => $"MCP Streamable HTTP | {b.DisplayName}")
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status404NotFound, typeof(JsonRpcError), contentTypes: ["application/json"]));

        streamableHttpGroup.MapPost("", streamableHttpHandler.HandlePostRequestAsync)
            .WithMetadata(new AcceptsMetadata(["application/json"]))
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, contentTypes: ["text/event-stream"]))
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status202Accepted));

        if (!streamableHttpHandler.HttpServerTransportOptions.Stateless)
        {
            // The GET and DELETE endpoints are not mapped in Stateless mode since there's no way to send unsolicited messages
            // for the GET to handle, and there is no server-side state for the DELETE to clean up.
            streamableHttpGroup.MapGet("", streamableHttpHandler.HandleGetRequestAsync)
                .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, contentTypes: ["text/event-stream"]));
            streamableHttpGroup.MapDelete("", streamableHttpHandler.HandleDeleteRequestAsync);

            // Map legacy HTTP with SSE endpoints only if not in Stateless mode, because we cannot guarantee the /message requests
            // will be handled by the same process as the /sse request.
            var sseHandler = endpoints.ServiceProvider.GetRequiredService<SseHandler>();
            var sseGroup = mcpGroup.MapGroup("")
                .WithDisplayName(b => $"MCP HTTP with SSE | {b.DisplayName}");

            sseGroup.MapGet("/sse", sseHandler.HandleSseRequestAsync)
                .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, contentTypes: ["text/event-stream"]));
            sseGroup.MapPost("/message", sseHandler.HandleMessageRequestAsync)
                .WithMetadata(new AcceptsMetadata(["application/json"]))
                .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status202Accepted));
        }

        return mcpGroup;
    }
}
