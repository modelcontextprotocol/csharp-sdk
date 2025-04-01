using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils.Json;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/> to add MCP endpoints.
/// </summary>
public static class McpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Sets up endpoints for handling MCP HTTP Streaming transport.
    /// </summary>
    /// <param name="endpoints">The web application to attach MCP HTTP endpoints.</param>
    /// <param name="runSession">Provides an optional asynchronous callback for handling new MCP sessions.</param>
    /// <returns>Returns a builder for configuring additional endpoint conventions like authorization policies.</returns>
    public static IEndpointConventionBuilder MapMcp(this IEndpointRouteBuilder endpoints, Func<HttpContext, IMcpServer, CancellationToken, Task>? runSession = null)
    {
        ConcurrentDictionary<string, SseResponseStreamTransport> _sessions = new(StringComparer.Ordinal);

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var mcpServerOptions = endpoints.ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>();

        var routeGroup = endpoints.MapGroup("");

        routeGroup.MapGet("/sse", async context =>
        {
            var response = context.Response;
            var requestAborted = context.RequestAborted;

            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-store";

            var sessionId = MakeNewSessionId();
            await using var transport = new SseResponseStreamTransport(response.Body, $"/message?sessionId={sessionId}");
            if (!_sessions.TryAdd(sessionId, transport))
            {
                throw new Exception($"Unreachable given good entropy! Session with ID '{sessionId}' has already been created.");
            }
            await using var server = McpServerFactory.Create(transport, mcpServerOptions.Value, loggerFactory, endpoints.ServiceProvider);

            try
            {
                var transportTask = transport.RunAsync(cancellationToken: requestAborted);

                try
                {
                    runSession ??= RunSession;
                    await runSession(context, server, requestAborted);
                }
                finally
                {
                    await transport.DisposeAsync();
                    await transportTask;
                }
            }
            catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
            {
                // RequestAborted always triggers when the client disconnects before a complete response body is written,
                // but this is how SSE connections are typically closed.
            }
            finally
            {
                _sessions.TryRemove(sessionId, out _);
            }
        });

        routeGroup.MapPost("/message", async context =>
        {
            if (!context.Request.Query.TryGetValue("sessionId", out var sessionId))
            {
                await Results.BadRequest("Missing sessionId query parameter.").ExecuteAsync(context);
                return;
            }

            if (!_sessions.TryGetValue(sessionId.ToString(), out var transport))
            {
                await Results.BadRequest($"Session {sessionId} not found.").ExecuteAsync(context);
                return;
            }

            var message = (IJsonRpcMessage?)await context.Request.ReadFromJsonAsync(McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IJsonRpcMessage)), context.RequestAborted);
            if (message is null)
            {
                await Results.BadRequest("No message in request body.").ExecuteAsync(context);
                return;
            }

            await transport.OnMessageReceivedAsync(message, context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsync("Accepted");
        });

        return routeGroup;
    }

    private static Task RunSession(HttpContext httpContext, IMcpServer session, CancellationToken requestAborted)
        => session.RunAsync(requestAborted);

    private static string MakeNewSessionId()
    {
        // 128 bits
        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        return WebEncoders.Base64UrlEncode(buffer);
    }
}
