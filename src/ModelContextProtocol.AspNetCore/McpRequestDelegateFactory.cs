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
    /// Creates a request delegate that handles MCP Streamable HTTP requests.
    /// </summary>
    /// <param name="serverOptions">Explicit MCP server options to use instead of options from dependency injection.</param>
    /// <param name="transportOptions">Explicit HTTP transport options to use instead of options from dependency injection.</param>
    /// <param name="serviceProvider">
    /// Optional service provider used to resolve singleton dependencies outside the delegate closure.
    /// If <see langword="null"/>, dependencies are resolved from <see cref="HttpContext.RequestServices"/>.
    /// </param>
    /// <returns>A request delegate that dispatches to MCP Streamable HTTP handlers based on HTTP method.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="serverOptions"/> and <paramref name="transportOptions"/> cannot be used together when
    /// <see cref="HttpServerTransportOptions.ConfigureSessionOptions"/> is non-null.
    /// </exception>
    public static RequestDelegate Create(
        McpServerOptions? serverOptions = null,
        HttpServerTransportOptions? transportOptions = null,
        IServiceProvider? serviceProvider = null)
    {
        if (serverOptions is not null && transportOptions?.ConfigureSessionOptions is not null)
        {
            throw new ArgumentException(ConfigureSessionOptionsRequiresClonedServerOptionsMessage, nameof(transportOptions));
        }

        var streamableHttpHandler = serviceProvider?.GetService<StreamableHttpHandler>() ??
            (serviceProvider is not null
                ? throw new InvalidOperationException(MissingHttpTransportServicesMessage)
                : null);

        return async context =>
        {
            var resolvedHandler = streamableHttpHandler ?? context.RequestServices.GetService<StreamableHttpHandler>() ??
                throw new InvalidOperationException(MissingHttpTransportServicesMessage);

            if (HttpMethods.IsPost(context.Request.Method))
            {
                await resolvedHandler.HandlePostRequestAsync(context, serverOptions, transportOptions);
            }
            else if (HttpMethods.IsGet(context.Request.Method))
            {
                await resolvedHandler.HandleGetRequestAsync(context, serverOptions, transportOptions);
            }
            else if (HttpMethods.IsDelete(context.Request.Method))
            {
                await resolvedHandler.HandleDeleteRequestAsync(context, serverOptions, transportOptions);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            }
        };
    }
}
