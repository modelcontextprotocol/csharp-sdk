using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ModelContextProtocol.AspNetCore;

internal sealed class StreamableHttpHandler(
    IOptions<McpServerOptions> mcpServerOptionsSnapshot,
    IOptionsFactory<McpServerOptions> mcpServerOptionsFactory,
    IOptions<HttpServerTransportOptions> httpMcpServerOptions,
    IHostApplicationLifetime hostApplicationLifetime,
    ILoggerFactory loggerFactory)
{

    private readonly ConcurrentDictionary<string, HttpMcpSession> _sessions = new(StringComparer.Ordinal);
    private readonly ILogger _logger = loggerFactory.CreateLogger<StreamableHttpHandler>();

    public async Task HandleRequestAsync(HttpContext context)
    {
        if (context.Request.Method == HttpMethods.Get)
        {
            await HandleSseRequestAsync(context);
        }
        else if (context.Request.Method == HttpMethods.Post)
        {
            await HandleMessageRequestAsync(context);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            await context.Response.WriteAsync("Method Not Allowed");
        }
    }

    public async Task HandleSseRequestAsync(HttpContext context)
    {
        // If the server is shutting down, we need to cancel all SSE connections immediately without waiting for HostOptions.ShutdownTimeout
        // which defaults to 30 seconds.
        using var sseCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, hostApplicationLifetime.ApplicationStopping);
        var cancellationToken = sseCts.Token;

        var response = context.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache,no-store";

        // Make sure we disable all response buffering for SSE
        context.Response.Headers.ContentEncoding = "identity";
        context.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();

        var sessionId = MakeNewSessionId();
        await using var transport = new SseResponseStreamTransport(response.Body, $"message?sessionId={sessionId}");
        var httpMcpSession = new HttpMcpSession(transport, context.User);
        if (!_sessions.TryAdd(sessionId, httpMcpSession))
        {
            Debug.Fail("Unreachable given good entropy!");
            throw new InvalidOperationException($"Session with ID '{sessionId}' has already been created.");
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
                await using var mcpServer = McpServerFactory.Create(transport, mcpServerOptions, context.Request, loggerFactory, context.RequestServices);
                context.Features.Set(mcpServer);

                var runSessionAsync = httpMcpServerOptions.Value.RunSessionHandler ?? RunSessionAsync;
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

        var message = (IJsonRpcMessage?)await context.Request.ReadFromJsonAsync(McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IJsonRpcMessage)), context.RequestAborted);
        if (message is null)
        {
            await Results.BadRequest("No message in request body.").ExecuteAsync(context);
            return;
        }

        await httpMcpSession.Transport.OnMessageReceivedAsync(message, context.RequestAborted);
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        await context.Response.WriteAsync("Accepted");
    }

    private static Task RunSessionAsync(HttpContext httpContext, IMcpServer session, CancellationToken requestAborted)
        => session.RunAsync(requestAborted);

    private static string MakeNewSessionId()
    {
        // 128 bits
        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        return WebEncoders.Base64UrlEncode(buffer);
    }
}
