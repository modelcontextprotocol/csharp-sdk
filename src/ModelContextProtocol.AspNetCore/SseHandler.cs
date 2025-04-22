using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore;

internal sealed class SseHandler(
    IOptions<McpServerOptions> mcpServerOptionsSnapshot,
    IOptionsFactory<McpServerOptions> mcpServerOptionsFactory,
    IOptions<HttpServerTransportOptions> httpMcpServerOptions,
    IHostApplicationLifetime hostApplicationLifetime,
    ILoggerFactory loggerFactory)
{
    private readonly ConcurrentDictionary<string, HttpMcpSession<SseResponseStreamTransport>> _sessions = new(StringComparer.Ordinal);

    public async Task HandleSseRequestAsync(HttpContext context)
    {
        var sessionId = StreamableHttpHandler.MakeNewSessionId();

        // If the server is shutting down, we need to cancel all SSE connections immediately without waiting for HostOptions.ShutdownTimeout
        // which defaults to 30 seconds.
        using var sseCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, hostApplicationLifetime.ApplicationStopping);
        var cancellationToken = sseCts.Token;

        StreamableHttpHandler.InitializeSseResponse(context);

        var requestPath = (context.Request.PathBase + context.Request.Path).ToString();
        var endpointPattern = requestPath[..(requestPath.LastIndexOf('/') + 1)];
        await using var transport = new SseResponseStreamTransport(context.Response.Body, $"{endpointPattern}message?sessionId={sessionId}");
        await using var httpMcpSession = new HttpMcpSession<SseResponseStreamTransport>(sessionId, transport, context.User, httpMcpServerOptions.Value.TimeProvider);
        if (!_sessions.TryAdd(sessionId, httpMcpSession))
        {
            throw new UnreachableException($"Unreachable given good entropy! Session with ID '{sessionId}' has already been created.");
        }

        try
        {
            var mcpServerOptions = mcpServerOptionsSnapshot.Value;
            if (httpMcpServerOptions.Value.ConfigureSessionOptions is { } configureSessionOptions)
            {
                mcpServerOptions = mcpServerOptionsFactory.Create(Options.DefaultName);
                await configureSessionOptions(context, mcpServerOptions, cancellationToken);
            }

            var transportTask = transport.RunAsync(cancellationToken);

            try
            {
                await using var mcpServer = McpServerFactory.Create(transport, mcpServerOptions, loggerFactory, context.RequestServices);
                httpMcpSession.Server = mcpServer;
                context.Features.Set(mcpServer);

                var runSessionAsync = httpMcpServerOptions.Value.RunSessionHandler ?? StreamableHttpHandler.RunSessionAsync;
                httpMcpSession.ServerRunTask = runSessionAsync(context, mcpServer, cancellationToken);
                await httpMcpSession.ServerRunTask;
            }
            finally
            {
                await transport.DisposeAsync();
                await transportTask;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // RequestAborted always triggers when the client disconnects before a complete response body is written,
            // but this is how SSE connections are typically closed.
        }
        finally
        {
            _sessions.TryRemove(sessionId, out _);
        }
    }

    public async Task HandleMessageRequestAsync(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("sessionId", out var sessionId))
        {
            await Results.BadRequest("Missing sessionId query parameter.").ExecuteAsync(context);
            return;
        }

        if (!_sessions.TryGetValue(sessionId.ToString(), out var httpMcpSession))
        {
            await Results.BadRequest($"Session ID not found.").ExecuteAsync(context);
            return;
        }

        if (!httpMcpSession.HasSameUserId(context.User))
        {
            await Results.Forbid().ExecuteAsync(context);
            return;
        }

        var message = (JsonRpcMessage?)await context.Request.ReadFromJsonAsync(McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)), context.RequestAborted);
        if (message is null)
        {
            await Results.BadRequest("No message in request body.").ExecuteAsync(context);
            return;
        }

        // Store headers
        JsonRpcRequest contextedMessage = ((JsonRpcRequest)message);

        if (contextedMessage.Params != null)
        {

            var headersDictionary = context.Request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString()
            );
            var jsonTypeInfo = McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(Dictionary<string, string>))
                                as JsonTypeInfo<Dictionary<string, string>> ?? throw new InvalidOperationException("Failed to get typeinfo.");

            contextedMessage.Params["RequestHeaders"] = JsonSerializer.SerializeToNode(headersDictionary, jsonTypeInfo);
        }

        await httpMcpSession.Transport.OnMessageReceivedAsync(message, context.RequestAborted);
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        await context.Response.WriteAsync("Accepted");
    }
}
