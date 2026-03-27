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
    /// </summary>
    /// <param name="endpoints">The web application to attach MCP HTTP endpoints.</param>
    /// <param name="pattern">The route pattern prefix to map to.</param>
    /// <returns>Returns a builder for configuring additional endpoint conventions like authorization policies.</returns>
    /// <exception cref="InvalidOperationException">The required MCP services have not been registered. Ensure <see cref="HttpMcpServerBuilderExtensions.WithHttpTransport"/> has been called during application startup.</exception>
    /// <remarks>
    /// For details about the Streamable HTTP transport, see the <see href="https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http">2025-11-25 protocol specification</see>.
    /// When legacy SSE is enabled via <see cref="HttpServerTransportOptions.EnableLegacySse"/>, this method also maps legacy SSE endpoints at the path "/sse" and "/message". For details about the HTTP with SSE transport, see the <see href="https://modelcontextprotocol.io/specification/2024-11-05/basic/transports#http-with-sse">2024-11-05 protocol specification</see>.
    /// </remarks>
    public static IEndpointConventionBuilder MapMcp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
    {
        var streamableHttpHandler = endpoints.ServiceProvider.GetService<StreamableHttpHandler>() ??
            throw new InvalidOperationException("You must call WithHttpTransport(). Unable to find required services. Call builder.Services.AddMcpServer().WithHttpTransport() in application startup code.");

        var options = streamableHttpHandler.HttpServerTransportOptions;

#pragma warning disable MCP9003 // EnableLegacySse - reading the obsolete property to check if SSE is enabled
        if (options.Stateless && options.EnableLegacySse)
        {
            throw new InvalidOperationException(
                "Legacy SSE endpoints cannot be enabled in stateless mode because SSE requires in-memory session state " +
                "shared between the GET /sse and POST /message requests. Remove the EnableLegacySse setting or disable stateless mode.");
        }
#pragma warning restore MCP9003

        var mcpGroup = endpoints.MapGroup(pattern);
        var streamableHttpGroup = mcpGroup.MapGroup("")
            .WithDisplayName(b => $"MCP Streamable HTTP | {b.DisplayName}")
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status404NotFound, typeof(JsonRpcError), contentTypes: ["application/json"]));

        streamableHttpGroup.MapPost("", streamableHttpHandler.HandlePostRequestAsync)
            .WithMetadata(new AcceptsMetadata(["application/json"]))
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, contentTypes: ["text/event-stream"]))
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status202Accepted));

        if (!options.Stateless)
        {
            // The GET endpoint is not mapped in Stateless mode since there's no way to send unsolicited messages.
            // Resuming streams via GET is currently not supported in Stateless mode.
            streamableHttpGroup.MapGet("", streamableHttpHandler.HandleGetRequestAsync)
                .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, contentTypes: ["text/event-stream"]));

            // The DELETE endpoint is not mapped in Stateless mode since there is no server-side state for the DELETE to clean up.
            streamableHttpGroup.MapDelete("", streamableHttpHandler.HandleDeleteRequestAsync);

#pragma warning disable MCP9003 // EnableLegacySse - reading the obsolete property to check if SSE is enabled
            if (options.EnableLegacySse)
#pragma warning restore MCP9003
            {
                // Map legacy HTTP with SSE endpoints. These are disabled by default because the SSE transport
                // has no built-in request backpressure (POST returns 202 immediately). Enable only for trusted clients.
                var sseHandler = endpoints.ServiceProvider.GetRequiredService<SseHandler>();
                var sseGroup = mcpGroup.MapGroup("")
                    .WithDisplayName(b => $"MCP HTTP with SSE | {b.DisplayName}");

                sseGroup.MapGet("/sse", sseHandler.HandleSseRequestAsync)
                    .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, contentTypes: ["text/event-stream"]));
                sseGroup.MapPost("/message", sseHandler.HandleMessageRequestAsync)
                    .WithMetadata(new AcceptsMetadata(["application/json"]))
                    .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status202Accepted));
            }
        }

        return mcpGroup;
    }
}
