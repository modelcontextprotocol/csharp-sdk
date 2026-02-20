using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Provides factory methods for creating request delegates that handle MCP HTTP endpoints.
/// </summary>
public static class McpRequestDelegateFactory
{
    private const string MissingHttpTransportServicesMessage =
        "You must call WithHttpTransport(). Unable to find required services. Call builder.Services.AddMcpServer().WithHttpTransport() in application startup code.";
    private const string ConfigureSessionOptionsRequiresClonedServerOptionsMessage =
        $"Explicit {nameof(McpServerOptions)} cannot be used with non-null {nameof(HttpServerTransportOptions.ConfigureSessionOptions)} because per-request cloning is required.";

    /// <summary>
    /// Creates a request delegate that handles MCP Streamable HTTP and legacy SSE endpoints.
    /// </summary>
    /// <param name="serverOptions">Explicit MCP server options to use instead of options from dependency injection.</param>
    /// <param name="transportOptions">Explicit HTTP transport options to use instead of options from dependency injection.</param>
    /// <returns>A request delegate that dispatches to MCP handlers based on HTTP method and request path.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="serverOptions"/> and <paramref name="transportOptions"/> cannot be used together when
    /// <see cref="HttpServerTransportOptions.ConfigureSessionOptions"/> is non-null.
    /// </exception>
    public static RequestDelegate Create(McpServerOptions? serverOptions = null, HttpServerTransportOptions? transportOptions = null)
    {
        if (serverOptions is not null && transportOptions?.ConfigureSessionOptions is not null)
        {
            throw new ArgumentException(ConfigureSessionOptionsRequiresClonedServerOptionsMessage, nameof(transportOptions));
        }

        return async context =>
        {
            var services = context.GetEndpoint()?.Metadata.GetMetadata<ServiceProviderMetadata>()?.ServiceProvider ?? context.RequestServices;
            var streamableHttpHandler = services.GetService<StreamableHttpHandler>() ??
                throw new InvalidOperationException(MissingHttpTransportServicesMessage);

            if (IsSseEndpoint(context.Request.Path))
            {
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    var sseHandler = services.GetRequiredService<SseHandler>();
                    await sseHandler.HandleSseRequestAsync(context, serverOptions, transportOptions);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                }

                return;
            }

            if (IsSseMessageEndpoint(context.Request.Path))
            {
                if (HttpMethods.IsPost(context.Request.Method))
                {
                    var sseHandler = services.GetRequiredService<SseHandler>();
                    await sseHandler.HandleMessageRequestAsync(context);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                }

                return;
            }

            if (HttpMethods.IsPost(context.Request.Method))
            {
                await streamableHttpHandler.HandlePostRequestAsync(context, serverOptions, transportOptions);
            }
            else if (HttpMethods.IsGet(context.Request.Method))
            {
                await streamableHttpHandler.HandleGetRequestAsync(context, serverOptions, transportOptions);
            }
            else if (HttpMethods.IsDelete(context.Request.Method))
            {
                await streamableHttpHandler.HandleDeleteRequestAsync(context, serverOptions, transportOptions);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            }
        };
    }

    private static bool IsSseEndpoint(PathString path)
        => path.HasValue && path.Value!.EndsWith("/sse", StringComparison.Ordinal);

    private static bool IsSseMessageEndpoint(PathString path)
        => path.HasValue && path.Value!.EndsWith("/message", StringComparison.Ordinal);

    internal sealed record ServiceProviderMetadata(IServiceProvider ServiceProvider);
}
