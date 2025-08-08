using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Default implementation that wires up the MCP Streamable HTTP transport endpoints
/// (and legacy SSE endpoints when applicable).
/// </summary>
internal sealed class DefaultMcpEndpointRouteBuilderConfigurator(
    StreamableHttpHandler streamableHttpHandler
) : IMcpEndpointRouteBuilderConfigurator
{
    public IEndpointConventionBuilder Configure(IEndpointRouteBuilder endpoints, string pattern)
    {
        var mcpGroup = endpoints.MapGroup(pattern);
        var streamableHttpGroup = mcpGroup
            .MapGroup("")
            .WithDisplayName(b => $"MCP Streamable HTTP | {b.DisplayName}")
            .WithMetadata(
                new ProducesResponseTypeMetadata(
                    StatusCodes.Status404NotFound,
                    typeof(JsonRpcError),
                    contentTypes: ["application/json"]
                )
            );

        streamableHttpGroup
            .MapPost("", streamableHttpHandler.HandlePostRequestAsync)
            .WithMetadata(new AcceptsMetadata(["application/json"]))
            .WithMetadata(
                new ProducesResponseTypeMetadata(
                    StatusCodes.Status200OK,
                    contentTypes: ["text/event-stream"]
                )
            )
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status202Accepted));

        if (!streamableHttpHandler.HttpServerTransportOptions.Stateless)
        {
            // The GET and DELETE endpoints are not mapped in Stateless mode since there's no way to send unsolicited messages
            // for the GET to handle, and there is no server-side state for the DELETE to clean up.
            streamableHttpGroup
                .MapGet("", streamableHttpHandler.HandleGetRequestAsync)
                .WithMetadata(
                    new ProducesResponseTypeMetadata(
                        StatusCodes.Status200OK,
                        contentTypes: ["text/event-stream"]
                    )
                );
            streamableHttpGroup.MapDelete("", streamableHttpHandler.HandleDeleteRequestAsync);

            // Map legacy HTTP with SSE endpoints only if not in Stateless mode, because we cannot guarantee the /message requests
            // will be handled by the same process as the /sse request.
            var sseHandler = endpoints.ServiceProvider.GetRequiredService<SseHandler>();
            var sseGroup = mcpGroup
                .MapGroup("")
                .WithDisplayName(b => $"MCP HTTP with SSE | {b.DisplayName}");

            sseGroup
                .MapGet("/sse", sseHandler.HandleSseRequestAsync)
                .WithMetadata(
                    new ProducesResponseTypeMetadata(
                        StatusCodes.Status200OK,
                        contentTypes: ["text/event-stream"]
                    )
                );
            sseGroup
                .MapPost("/message", sseHandler.HandleMessageRequestAsync)
                .WithMetadata(new AcceptsMetadata(["application/json"]))
                .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status202Accepted));
        }

        return mcpGroup;
    }

    IEndpointConventionBuilder IMcpEndpointRouteBuilderConfigurator.Configure(IEndpointRouteBuilder endpoints, string pattern)
    {
        throw new NotImplementedException();
    }
}
