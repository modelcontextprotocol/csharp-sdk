using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Stateless;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore;

internal sealed class StreamableHttpHandler(
    IOptions<McpServerOptions> mcpServerOptionsSnapshot,
    IOptionsFactory<McpServerOptions> mcpServerOptionsFactory,
    IOptions<HttpServerTransportOptions> httpServerTransportOptions,
    IDataProtectionProvider dataProtection,
    ILoggerFactory loggerFactory,
    IServiceProvider applicationServices)
{
    private const string McpSessionIdHeaderName = "Mcp-Session-Id";
    private static readonly JsonTypeInfo<JsonRpcError> s_errorTypeInfo = GetRequiredJsonTypeInfo<JsonRpcError>();

    public ConcurrentDictionary<string, HttpMcpSession<StreamableHttpServerTransport>> Sessions { get; } = new(StringComparer.Ordinal);

    public HttpServerTransportOptions HttpServerTransportOptions => httpServerTransportOptions.Value;

    private IDataProtector Protector { get; } = dataProtection.CreateProtector("Microsoft.AspNetCore.StreamableHttpHandler.StatelessSessionId");

    public async Task HandlePostRequestAsync(HttpContext context)
    {
        // The Streamable HTTP spec mandates the client MUST accept both application/json and text/event-stream.
        // ASP.NET Core Minimal APIs mostly try to stay out of the business of response content negotiation,
        // so we have to do this manually. The spec doesn't mandate that servers MUST reject these requests,
        // but it's probably good to at least start out trying to be strict.
        var typedHeaders = context.Request.GetTypedHeaders();
        if (!typedHeaders.Accept.Any(MatchesApplicationJsonMediaType) || !typedHeaders.Accept.Any(MatchesTextEventStreamMediaType))
        {
            await WriteJsonRpcErrorAsync(context,
                "Not Acceptable: Client must accept both application/json and text/event-stream",
                StatusCodes.Status406NotAcceptable);
            return;
        }

        var session = await GetOrCreateSessionAsync(context);
        if (session is null)
        {
            return;
        }

        try
        {
            using var _ = session.AcquireReference();

            InitializeSseResponse(context);
            var wroteResponse = await session.Transport.HandlePostRequest(new HttpDuplexPipe(context), context.RequestAborted);
            if (!wroteResponse)
            {
                // We wound up writing nothing, so there should be no Content-Type response header.
                context.Response.Headers.ContentType = (string?)null;
                context.Response.StatusCode = StatusCodes.Status202Accepted;
            }
        }
        finally
        {
            // Stateless sessions are 1:1 with HTTP requests and are outlived by the MCP session tracked by the Mcp-Session-Id.
            // Non-stateless sessions are 1:1 with the Mcp-Session-Id and outlive the POST request.
            // Non-stateless sessions get disposed by a DELETE request or the IdleTrackingBackgroundService.
            if (HttpServerTransportOptions.Stateless)
            {
                await session.DisposeAsync();
            }
        }
    }

    public async Task HandleGetRequestAsync(HttpContext context)
    {
        if (!context.Request.GetTypedHeaders().Accept.Any(MatchesTextEventStreamMediaType))
        {
            await WriteJsonRpcErrorAsync(context,
                "Not Acceptable: Client must accept text/event-stream",
                StatusCodes.Status406NotAcceptable);
            return;
        }

        var sessionId = context.Request.Headers[McpSessionIdHeaderName].ToString();
        var session = await GetSessionAsync(context, sessionId);
        if (session is null)
        {
            return;
        }

        if (!session.TryStartGetRequest())
        {
            await WriteJsonRpcErrorAsync(context,
                "Bad Request: This server does not support multiple GET requests. Start a new session to get a new GET SSE response.",
                StatusCodes.Status400BadRequest);
            return;
        }

        using var _ = session.AcquireReference();
        InitializeSseResponse(context);

        // We should flush headers to indicate a 200 success quickly, because the initialization response
        // will be sent in response to a different POST request. It might be a while before we send a message
        // over this response body.
        await context.Response.Body.FlushAsync(context.RequestAborted);
        await session.Transport.HandleGetRequest(context.Response.Body, context.RequestAborted);
    }

    public async Task HandleDeleteRequestAsync(HttpContext context)
    {
        var sessionId = context.Request.Headers[McpSessionIdHeaderName].ToString();
        if (Sessions.TryRemove(sessionId, out var session))
        {
            await session.DisposeAsync();
        }
    }

    private async ValueTask<HttpMcpSession<StreamableHttpServerTransport>?> GetSessionAsync(HttpContext context, string sessionId)
    {
        HttpMcpSession<StreamableHttpServerTransport>? session;

        if (HttpServerTransportOptions.Stateless)
        {
            var sessionJson = Protector.Unprotect(sessionId);
            var statelessSessionId = JsonSerializer.Deserialize(sessionJson, StatelessSessionIdJsonContext.Default.StatelessSessionId);
            var transport = new StreamableHttpServerTransport
            {
                Stateless = true,
                SessionId = sessionId,
            };
            session = await CreateSessionAsync(context, transport, sessionId, statelessSessionId);
        }
        else if (!Sessions.TryGetValue(sessionId, out session))
        {
            // -32001 isn't part of the MCP standard, but this is what the typescript-sdk currently does.
            // One of the few other usages I found was from some Ethereum JSON-RPC documentation and this
            // JSON-RPC library from Microsoft called StreamJsonRpc where it's called JsonRpcErrorCode.NoMarshaledObjectFound
            // https://learn.microsoft.com/dotnet/api/streamjsonrpc.protocol.jsonrpcerrorcode?view=streamjsonrpc-2.9#fields
            await WriteJsonRpcErrorAsync(context, "Session not found", StatusCodes.Status404NotFound, -32001);
            return null;
        }

        if (!session.HasSameUserId(context.User))
        {
            await WriteJsonRpcErrorAsync(context,
                "Forbidden: The currently authenticated user does not match the user who initiated the session.",
                StatusCodes.Status403Forbidden);
            return null;
        }

        context.Response.Headers[McpSessionIdHeaderName] = session.Id;
        context.Features.Set(session.Server);
        return session;
    }

    private async ValueTask<HttpMcpSession<StreamableHttpServerTransport>?> GetOrCreateSessionAsync(HttpContext context)
    {
        var sessionId = context.Request.Headers[McpSessionIdHeaderName].ToString();

        if (string.IsNullOrEmpty(sessionId))
        {
            return await StartNewSessionAsync(context);
        }
        else
        {
            return await GetSessionAsync(context, sessionId);
        }
    }

    private async ValueTask<HttpMcpSession<StreamableHttpServerTransport>> StartNewSessionAsync(HttpContext context)
    {
        string sessionId;
        StreamableHttpServerTransport transport;

        if (!HttpServerTransportOptions.Stateless)
        {
            sessionId = MakeNewSessionId();
            transport = new()
            {
                SessionId = sessionId,
                FlowExecutionContextFromRequests = !HttpServerTransportOptions.PerSessionExecutionContext,
            };
            context.Response.Headers[McpSessionIdHeaderName] = sessionId;
        }
        else
        {
            // "(uninitialized stateless id)" is not written anywhere. We delay writing the MCP-Session-Id
            // until after we receive the initialize request with the client info we need to serialize.
            sessionId = "(uninitialized stateless id)";
            transport = new()
            {
                Stateless = true,
            };
            ScheduleStatelessSessionIdWrite(context, transport);
        }

        var session = await CreateSessionAsync(context, transport, sessionId);

        // The HttpMcpSession is not stored between requests in stateless mode. Instead, the session is recreated from the MCP-Session-Id.
        if (!HttpServerTransportOptions.Stateless)
        {
            if (!Sessions.TryAdd(sessionId, session))
            {
                throw new UnreachableException($"Unreachable given good entropy! Session with ID '{sessionId}' has already been created.");
            }
        }

        return session;
    }

    private async ValueTask<HttpMcpSession<StreamableHttpServerTransport>> CreateSessionAsync(
        HttpContext context,
        StreamableHttpServerTransport transport,
        string sessionId,
        StatelessSessionId? statelessId = null)
    {
        var mcpServerServices = applicationServices;
        var mcpServerOptions = mcpServerOptionsSnapshot.Value;
        if (statelessId is not null || HttpServerTransportOptions.ConfigureSessionOptions is not null)
        {
            mcpServerOptions = mcpServerOptionsFactory.Create(Options.DefaultName);

            if (statelessId is not null)
            {
                // The session does not outlive the request in stateless mode.
                mcpServerServices = context.RequestServices;
                mcpServerOptions.ScopeRequests = false;
                mcpServerOptions.KnownClientInfo = statelessId.ClientInfo;
            }

            if (HttpServerTransportOptions.ConfigureSessionOptions is { } configureSessionOptions)
            {
                await configureSessionOptions(context, mcpServerOptions, context.RequestAborted);
            }
        }

        var server = McpServerFactory.Create(transport, mcpServerOptions, loggerFactory, mcpServerServices);
        context.Features.Set(server);

        var userIdClaim = statelessId?.UserIdClaim ?? GetUserIdClaim(context.User);
        var session = new HttpMcpSession<StreamableHttpServerTransport>(sessionId, transport, userIdClaim, HttpServerTransportOptions.TimeProvider)
        {
            Server = server,
        };

        var runSessionAsync = HttpServerTransportOptions.RunSessionHandler ?? RunSessionAsync;
        session.ServerRunTask = runSessionAsync(context, server, session.SessionClosed);

        return session;
    }

    private static Task WriteJsonRpcErrorAsync(HttpContext context, string errorMessage, int statusCode, int errorCode = -32000)
    {
        var jsonRpcError = new JsonRpcError
        {
            Error = new()
            {
                Code = errorCode,
                Message = errorMessage,
            },
        };
        return Results.Json(jsonRpcError, s_errorTypeInfo, statusCode: statusCode).ExecuteAsync(context);
    }

    internal static void InitializeSseResponse(HttpContext context)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache,no-store";

        // Make sure we disable all response buffering for SSE.
        context.Response.Headers.ContentEncoding = "identity";
        context.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();
    }

    internal static string MakeNewSessionId()
    {
        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        return WebEncoders.Base64UrlEncode(buffer);
    }

    private void ScheduleStatelessSessionIdWrite(HttpContext context, StreamableHttpServerTransport transport)
    {
        transport.OnInitRequestReceived = initRequestParams =>
        {
            var statelessId = new StatelessSessionId
            {
                ClientInfo = initRequestParams?.ClientInfo,
                UserIdClaim = GetUserIdClaim(context.User),
            };

            var sessionJson = JsonSerializer.Serialize(statelessId, StatelessSessionIdJsonContext.Default.StatelessSessionId);
            transport.SessionId = Protector.Protect(sessionJson);
            context.Response.Headers[McpSessionIdHeaderName] = transport.SessionId;
            return ValueTask.CompletedTask;
        };
    }

    internal static Task RunSessionAsync(HttpContext httpContext, IMcpServer session, CancellationToken requestAborted)
        => session.RunAsync(requestAborted);

    // SignalR only checks for ClaimTypes.NameIdentifier in HttpConnectionDispatcher, but AspNetCore.Antiforgery checks that plus the sub and UPN claims.
    // However, we short-circuit unlike antiforgery since we expect to call this to verify MCP messages a lot more frequently than
    // verifying antiforgery tokens from <form> posts.
    internal static UserIdClaim? GetUserIdClaim(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var claim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.Upn);

        if (claim is { } idClaim)
        {
            return new(idClaim.Type, idClaim.Value, idClaim.Issuer);
        }

        return null;
    }

    private static JsonTypeInfo<T> GetRequiredJsonTypeInfo<T>() => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private static bool MatchesApplicationJsonMediaType(MediaTypeHeaderValue acceptHeaderValue)
        => acceptHeaderValue.MatchesMediaType("application/json");

    private static bool MatchesTextEventStreamMediaType(MediaTypeHeaderValue acceptHeaderValue)
        => acceptHeaderValue.MatchesMediaType("text/event-stream");

    private sealed class HttpDuplexPipe(HttpContext context) : IDuplexPipe
    {
        public PipeReader Input => context.Request.BodyReader;
        public PipeWriter Output => context.Response.BodyWriter;
    }
}
