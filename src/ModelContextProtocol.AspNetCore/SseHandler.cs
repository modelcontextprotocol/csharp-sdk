using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ModelContextProtocol.AspNetCore;

internal sealed class SseHandler(
    IOptions<McpServerOptions> mcpServerOptionsSnapshot,
    IOptionsFactory<McpServerOptions> mcpServerOptionsFactory,
    IOptions<HttpServerTransportOptions> httpMcpServerOptions,
    IHostApplicationLifetime hostApplicationLifetime,
    ILoggerFactory loggerFactory)
{
    private readonly ConcurrentDictionary<string, SseSession> _sessions = new(StringComparer.Ordinal);

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
        await using var transport = new SseResponseStreamTransport(context.Response.Body, $"{endpointPattern}message?sessionId={sessionId}", sessionId);

        var userIdClaim = StreamableHttpHandler.GetUserIdClaim(context.User);
        var sseSession = new SseSession(transport, userIdClaim);

        if (!_sessions.TryAdd(sessionId, sseSession))
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
                await using var mcpServer = McpServer.Create(transport, mcpServerOptions, loggerFactory, context.RequestServices);
                context.Features.Set(mcpServer);

#pragma warning disable MCPEXP002 // RunSessionHandler is experimental
                var runSessionAsync = httpMcpServerOptions.Value.RunSessionHandler ?? StreamableHttpHandler.RunSessionAsync;
#pragma warning restore MCPEXP002
                await runSessionAsync(context, mcpServer, cancellationToken);
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

        if (!_sessions.TryGetValue(sessionId.ToString(), out var sseSession))
        {
            await Results.BadRequest($"Session ID not found.").ExecuteAsync(context);
            return;
        }

        if (sseSession.UserId != StreamableHttpHandler.GetUserIdClaim(context.User))
        {
            await Results.Forbid().ExecuteAsync(context);
            return;
        }

        var message = await StreamableHttpHandler.ReadJsonRpcMessageAsync(context);
        if (message is null)
        {
            await Results.BadRequest("No message in request body.").ExecuteAsync(context);
            return;
        }

        await sseSession.Transport.OnMessageReceivedAsync(message, context.RequestAborted);
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        await context.Response.WriteAsync("Accepted");
    }

    private record SseSession(SseResponseStreamTransport Transport, UserIdClaim? UserId);
}
