using System.Buffers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore;

internal sealed class StreamableHttpHandler(
    IOptions<McpServerOptions> mcpServerOptionsSnapshot,
    IOptionsFactory<McpServerOptions> mcpServerOptionsFactory,
    IOptions<HttpServerTransportOptions> httpServerTransportOptions,
    StatefulSessionManager sessionManager,
    IHostApplicationLifetime hostApplicationLifetime,
    IServiceProvider applicationServices,
    ILoggerFactory loggerFactory)
{
    private const string McpSessionIdHeaderName = McpHttpHeaders.SessionId;
    private const string McpProtocolVersionHeaderName = McpHttpHeaders.ProtocolVersion;
    private const string LastEventIdHeaderName = McpHttpHeaders.LastEventId;

    /// <summary>
    /// All protocol versions supported by this implementation.
    /// Keep in sync with McpSessionHandler.SupportedProtocolVersions in ModelContextProtocol.Core.
    /// </summary>
    private static readonly HashSet<string> s_supportedProtocolVersions =
    [
        "2024-11-05",
        "2025-03-26",
        "2025-06-18",
        "2025-11-25",
        McpHttpHeaders.DraftProtocolVersion,
    ];

    private static readonly JsonTypeInfo<JsonRpcMessage> s_messageTypeInfo = GetRequiredJsonTypeInfo<JsonRpcMessage>();
    private static readonly JsonTypeInfo<JsonRpcError> s_errorTypeInfo = GetRequiredJsonTypeInfo<JsonRpcError>();

    private static bool AllowNewSessionForNonInitializeRequests { get; } =
        AppContext.TryGetSwitch("ModelContextProtocol.AspNetCore.AllowNewSessionForNonInitializeRequests", out var enabled) && enabled;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _migrationLocks = new(StringComparer.Ordinal);

    public HttpServerTransportOptions HttpServerTransportOptions => httpServerTransportOptions.Value;

    public async Task HandlePostRequestAsync(HttpContext context)
    {
        if (!ValidateProtocolVersionHeader(context, out var protocolVersionError))
        {
            await WriteJsonRpcErrorDetailAsync(context, protocolVersionError, StatusCodes.Status400BadRequest);
            return;
        }

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

        var message = await ReadJsonRpcMessageAsync(context);
        if (message is null)
        {
            await WriteJsonRpcErrorAsync(context,
                "Bad Request: The POST body did not contain a valid JSON-RPC message.",
                StatusCodes.Status400BadRequest);
            return;
        }

        if (!ValidateMcpHeaders(context, message, mcpServerOptionsSnapshot.Value.ToolCollection, out var errorMessage))
        {
            await WriteJsonRpcErrorAsync(context, errorMessage, StatusCodes.Status400BadRequest, (int)McpErrorCode.HeaderMismatch);
            return;
        }

        var session = await GetOrCreateSessionAsync(context, message);
        if (session is null)
        {
            return;
        }

        await using var _ = await session.AcquireReferenceAsync(context.RequestAborted);

        InitializeSseResponse(context);
        var wroteResponse = await session.Transport.HandlePostRequestAsync(message, context.Response.Body, context.RequestAborted);
        if (!wroteResponse)
        {
            // We wound up writing nothing, so there should be no Content-Type response header.
            context.Response.Headers.ContentType = (string?)null;
            context.Response.StatusCode = StatusCodes.Status202Accepted;
        }
    }

    public async Task HandleGetRequestAsync(HttpContext context)
    {
        if (!ValidateProtocolVersionHeader(context, out var protocolVersionError))
        {
            await WriteJsonRpcErrorDetailAsync(context, protocolVersionError, StatusCodes.Status400BadRequest);
            return;
        }

        var sessionId = context.Request.Headers[McpSessionIdHeaderName].ToString();

        // Under the draft protocol revision (SEP-2575), the standalone HTTP GET endpoint for unsolicited
        // server-to-client messages is removed. Clients should use subscriptions/listen (POST) instead.
        // We only reject GET when the request looks like a draft-mode probe (experimental version with
        // no Mcp-Session-Id); legacy stateful sessions that opted into MRTR via the experimental version
        // are still allowed to use GET for back-compat.
        if (IsDraftProtocolRequest(context) && string.IsNullOrEmpty(sessionId))
        {
            await WriteJsonRpcErrorAsync(context,
                "Bad Request: The GET endpoint is not supported by the draft protocol revision. Use subscriptions/listen via POST instead.",
                StatusCodes.Status400BadRequest);
            return;
        }

        if (!context.Request.GetTypedHeaders().Accept.Any(MatchesTextEventStreamMediaType))
        {
            await WriteJsonRpcErrorAsync(context,
                "Not Acceptable: Client must accept text/event-stream",
                StatusCodes.Status406NotAcceptable);
            return;
        }

        var session = await GetSessionAsync(context, sessionId);
        if (session is null)
        {
            return;
        }

        var lastEventId = context.Request.Headers[LastEventIdHeaderName].ToString();
        if (!string.IsNullOrEmpty(lastEventId))
        {
            await HandleResumedStreamAsync(context, session, lastEventId);
        }
        else
        {
            await HandleUnsolicitedMessageStreamAsync(context, session);
        }
    }

    private async Task HandleResumedStreamAsync(HttpContext context, StreamableHttpSession session, string lastEventId)
    {
        if (HttpServerTransportOptions.Stateless)
        {
            await WriteJsonRpcErrorAsync(context,
                "Bad Request: The Last-Event-ID header is not supported in stateless mode.",
                StatusCodes.Status400BadRequest);
            return;
        }

        var eventStreamReader = await GetEventStreamReaderAsync(context, lastEventId);
        if (eventStreamReader is null)
        {
            // There was an error obtaining the event stream; consider the request failed.
            return;
        }

        if (!string.Equals(session.Id, eventStreamReader.SessionId, StringComparison.Ordinal))
        {
            await WriteJsonRpcErrorAsync(context,
                "Bad Request: The Last-Event-ID header refers to a session with a different session ID.",
                StatusCodes.Status400BadRequest);
            return;
        }

        using var sseCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, hostApplicationLifetime.ApplicationStopping);
        var cancellationToken = sseCts.Token;

        await using var _ = await session.AcquireReferenceAsync(cancellationToken);

        InitializeSseResponse(context);
        await eventStreamReader.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private async Task HandleUnsolicitedMessageStreamAsync(HttpContext context, StreamableHttpSession session)
    {
        if (!session.TryStartGetRequest())
        {
            await WriteJsonRpcErrorAsync(context,
                "Bad Request: This server does not support multiple GET requests. Start a new session or use Last-Event-ID header to resume.",
                StatusCodes.Status400BadRequest);
            return;
        }

        // Link the GET request to both RequestAborted and ApplicationStopping.
        // The GET request should complete immediately during graceful shutdown without waiting for
        // in-flight POST requests to complete. This prevents slow shutdown when clients are still connected.
        using var sseCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, hostApplicationLifetime.ApplicationStopping);
        var cancellationToken = sseCts.Token;

        try
        {
            await using var _ = await session.AcquireReferenceAsync(cancellationToken);
            InitializeSseResponse(context);
            await session.Transport.HandleGetRequestAsync(context.Response.Body, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // RequestAborted always triggers when the client disconnects before a complete response body is written,
            // but this is how SSE connections are typically closed.
        }
    }

    private static async Task HandleResumePostResponseStreamAsync(HttpContext context, ISseEventStreamReader eventStreamReader)
    {
        InitializeSseResponse(context);
        await eventStreamReader.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    public async Task HandleDeleteRequestAsync(HttpContext context)
    {
        if (!ValidateProtocolVersionHeader(context, out var protocolVersionError))
        {
            await WriteJsonRpcErrorDetailAsync(context, protocolVersionError, StatusCodes.Status400BadRequest);
            return;
        }

        var sessionId = context.Request.Headers[McpSessionIdHeaderName].ToString();

        // Under the draft revision there are no sessions to terminate. Reject DELETE requests that
        // declare the draft version without an Mcp-Session-Id. Legacy stateful sessions opted into
        // the draft version may still call DELETE for back-compat.
        if (IsDraftProtocolRequest(context) && string.IsNullOrEmpty(sessionId))
        {
            await WriteJsonRpcErrorAsync(context,
                "Bad Request: The DELETE endpoint is not supported by the draft protocol revision (no Mcp-Session-Id sessions exist).",
                StatusCodes.Status400BadRequest);
            return;
        }

        if (string.IsNullOrEmpty(sessionId) || !sessionManager.TryGetValue(sessionId, out var session))
        {
            return;
        }

        if (!session.HasSameUserId(context.User))
        {
            await WriteJsonRpcErrorAsync(context,
                "Forbidden: The currently authenticated user does not match the user who initiated the session.",
                StatusCodes.Status403Forbidden);
            return;
        }

        if (sessionManager.TryRemove(sessionId, out session))
        {
            await session.DisposeAsync();
        }
    }

    private async ValueTask<StreamableHttpSession?> GetSessionAsync(HttpContext context, string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            await WriteJsonRpcErrorAsync(context,
                "Bad Request: Mcp-Session-Id header is required for GET and DELETE requests when the server is using sessions. " +
                "If your server doesn't need sessions, enable stateless mode by setting HttpServerTransportOptions.Stateless = true. " +
                "See https://csharp.sdk.modelcontextprotocol.io/concepts/stateless/stateless.html for more details.",
                StatusCodes.Status400BadRequest);
            return null;
        }

        if (!sessionManager.TryGetValue(sessionId, out var session))
        {
            // Session not found locally. Attempt migration if a handler is registered.
            session = await TryMigrateSessionAsync(context, sessionId);

            if (session is null)
            {
                // -32001 isn't part of the MCP standard, but this is what the typescript-sdk currently does.
                // One of the few other usages I found was from some Ethereum JSON-RPC documentation and this
                // JSON-RPC library from Microsoft called StreamJsonRpc where it's called JsonRpcErrorCode.NoMarshaledObjectFound
                // https://learn.microsoft.com/dotnet/api/streamjsonrpc.protocol.jsonrpcerrorcode?view=streamjsonrpc-2.9#fields
                await WriteJsonRpcErrorAsync(context, "Session not found", StatusCodes.Status404NotFound, -32001);
                return null;
            }
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

    private async ValueTask<StreamableHttpSession?> TryMigrateSessionAsync(HttpContext context, string sessionId)
    {
#pragma warning disable MCP9005 // Stateful Streamable HTTP options are obsolete but still wired up internally.
        if (HttpServerTransportOptions.SessionMigrationHandler is not { } handler)
        {
            return null;
        }
#pragma warning restore MCP9005

        var migrationLock = _migrationLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await migrationLock.WaitAsync(context.RequestAborted);
        try
        {
            // Re-check after acquiring the lock - another thread may have already completed migration.
            if (sessionManager.TryGetValue(sessionId, out var session))
            {
                return session;
            }

            var initParams = await handler.AllowSessionMigrationAsync(context, sessionId, context.RequestAborted);
            if (initParams is null)
            {
                return null;
            }

            var migratedSession = await MigrateSessionAsync(context, sessionId, initParams);

            // Register the session with the session manager while still holding the lock
            // so concurrent requests for the same session ID find it via sessionManager.TryGetValue.
            await migratedSession.EnsureStartedAsync(context.RequestAborted);

            return migratedSession;
        }
        finally
        {
            migrationLock.Release();
            _migrationLocks.TryRemove(sessionId, out _);
        }
    }

    private async ValueTask<StreamableHttpSession?> GetOrCreateSessionAsync(HttpContext context, JsonRpcMessage message)
    {
        var sessionId = context.Request.Headers[McpSessionIdHeaderName].ToString();
        bool isDraftRequest = IsDraftProtocolRequest(context);

        // Under the draft protocol revision (SEP-2575 + SEP-2567), sessions are removed entirely.
        // A request that declares the experimental draft version via MCP-Protocol-Version and that
        // does NOT include an Mcp-Session-Id is treated as sessionless regardless of the
        // HttpServerTransportOptions.Stateless setting (which governs only legacy clients).
        //
        // For back-compat with clients that previously used the experimental version on top of the
        // legacy stateful session model (e.g., MRTR-as-extension-on-initialize), we still route
        // experimental-version requests that DO include an Mcp-Session-Id through the legacy session
        // lookup path. SEP-2567 will eventually phase that out, but we preserve it now to avoid
        // breaking existing consumers without forcing them to change their setup code.
        if (isDraftRequest && string.IsNullOrEmpty(sessionId))
        {
            return await StartNewSessionAsync(context, forceStateless: true);
        }

        if (string.IsNullOrEmpty(sessionId))
        {
            // In stateful mode, only allow creating new sessions for initialize requests.
            // In stateless mode, every request is independent, so we always create a new session.
            if (!HttpServerTransportOptions.Stateless && !AllowNewSessionForNonInitializeRequests
                && message is not JsonRpcRequest { Method: RequestMethods.Initialize })
            {
                await WriteJsonRpcErrorAsync(context,
                    "Bad Request: A new session can only be created by an initialize request. Include a valid Mcp-Session-Id header for non-initialize requests, " +
                    "or enable stateless mode by setting HttpServerTransportOptions.Stateless = true if your server doesn't need sessions. " +
                    "See https://csharp.sdk.modelcontextprotocol.io/concepts/stateless/stateless.html for more details.",
                    StatusCodes.Status400BadRequest);
                return null;
            }

            return await StartNewSessionAsync(context);
        }
        else if (HttpServerTransportOptions.Stateless)
        {
            // In stateless mode, we should not be getting existing sessions via sessionId
            // This path should not be reached in stateless mode
            await WriteJsonRpcErrorAsync(context, "Bad Request: The Mcp-Session-Id header is not supported in stateless mode", StatusCodes.Status400BadRequest);
            return null;
        }
        else
        {
            return await GetSessionAsync(context, sessionId);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the request declares the draft protocol revision via
    /// the <c>MCP-Protocol-Version</c> header. Draft requests are always sessionless and do not perform
    /// the legacy <c>initialize</c> handshake (SEP-2575 + SEP-2567).
    /// </summary>
    private static bool IsDraftProtocolRequest(HttpContext context)
    {
        var protocolVersionHeader = context.Request.Headers[McpProtocolVersionHeaderName].ToString();
        return string.Equals(protocolVersionHeader, McpHttpHeaders.DraftProtocolVersion, StringComparison.Ordinal);
    }

    private async ValueTask<StreamableHttpSession> StartNewSessionAsync(HttpContext context, bool forceStateless = false)
    {
        string sessionId;
        StreamableHttpServerTransport transport;

        bool isStateless = HttpServerTransportOptions.Stateless || forceStateless;

        if (!isStateless)
        {
            sessionId = MakeNewSessionId();
#pragma warning disable MCP9005 // Stateful Streamable HTTP options are obsolete but still wired up internally.
            transport = new(loggerFactory)
            {
                SessionId = sessionId,
                FlowExecutionContextFromRequests = !HttpServerTransportOptions.PerSessionExecutionContext,
                EventStreamStore = HttpServerTransportOptions.EventStreamStore,
                OnSessionInitialized = HttpServerTransportOptions.SessionMigrationHandler is { } handler
                    ? (initParams, ct) => handler.OnSessionInitializedAsync(context, sessionId, initParams, ct)
                    : null,
            };
#pragma warning restore MCP9005

            context.Response.Headers[McpSessionIdHeaderName] = sessionId;
        }
        else
        {
            // In stateless mode (legacy or draft), each request is independent. Don't set any session ID on the transport.
            // If in the future we support resuming stateless requests, we should populate
            // the event stream store and retry interval here as well.
            sessionId = "";
            transport = new(loggerFactory)
            {
                Stateless = true,
            };
        }

        return await CreateSessionAsync(context, transport, sessionId, forceStateless: forceStateless);
    }

    private async ValueTask<StreamableHttpSession> CreateSessionAsync(
        HttpContext context,
        StreamableHttpServerTransport transport,
        string sessionId,
        Action<McpServerOptions>? configureOptions = null,
        bool forceStateless = false)
    {
        var mcpServerServices = applicationServices;
        var mcpServerOptions = mcpServerOptionsSnapshot.Value;
        bool effectivelyStateless = HttpServerTransportOptions.Stateless || forceStateless;

        if (effectivelyStateless || HttpServerTransportOptions.ConfigureSessionOptions is not null || configureOptions is not null)
        {
            mcpServerOptions = mcpServerOptionsFactory.Create(Options.DefaultName);

            if (effectivelyStateless)
            {
                // The session does not outlive the request in stateless mode.
                mcpServerServices = context.RequestServices;
                mcpServerOptions.ScopeRequests = false;
            }

            configureOptions?.Invoke(mcpServerOptions);

            if (HttpServerTransportOptions.ConfigureSessionOptions is { } configureSessionOptions)
            {
                await configureSessionOptions(context, mcpServerOptions, context.RequestAborted);
            }
        }

        var server = McpServer.Create(transport, mcpServerOptions, loggerFactory, mcpServerServices);
        context.Features.Set(server);

        var userIdClaim = GetUserIdClaim(context.User);
        var session = new StreamableHttpSession(sessionId, transport, server, userIdClaim, sessionManager);

#pragma warning disable MCPEXP002 // RunSessionHandler is experimental
        var runSessionAsync = HttpServerTransportOptions.RunSessionHandler ?? RunSessionAsync;
#pragma warning restore MCPEXP002
        session.ServerRunTask = runSessionAsync(context, server, session.SessionClosed);

        return session;
    }

    private async ValueTask<StreamableHttpSession> MigrateSessionAsync(
        HttpContext context,
        string sessionId,
        InitializeRequestParams initializeParams)
    {
        var transport = new StreamableHttpServerTransport(loggerFactory)
        {
            SessionId = sessionId,
#pragma warning disable MCP9005 // Stateful Streamable HTTP options are obsolete but still wired up internally.
            FlowExecutionContextFromRequests = !HttpServerTransportOptions.PerSessionExecutionContext,
            EventStreamStore = HttpServerTransportOptions.EventStreamStore,
#pragma warning restore MCP9005
        };

        // Initialize the transport with the migrated session's init params.
        await transport.HandleInitializeRequestAsync(initializeParams);

        context.Response.Headers[McpSessionIdHeaderName] = sessionId;

        return await CreateSessionAsync(context, transport, sessionId, options =>
        {
            options.KnownClientInfo = initializeParams.ClientInfo;
            options.KnownClientCapabilities = initializeParams.Capabilities;
        });
    }

    private async ValueTask<ISseEventStreamReader?> GetEventStreamReaderAsync(HttpContext context, string lastEventId)
    {
#pragma warning disable MCP9005 // Stateful Streamable HTTP options are obsolete but still wired up internally.
        if (HttpServerTransportOptions.EventStreamStore is not { } eventStreamStore)
#pragma warning restore MCP9005
        {
            await WriteJsonRpcErrorAsync(context,
                "Bad Request: This server does not support resuming streams.",
                StatusCodes.Status400BadRequest);
            return null;
        }

        var eventStreamReader = await eventStreamStore.GetStreamReaderAsync(lastEventId, context.RequestAborted);
        if (eventStreamReader is null)
        {
            await WriteJsonRpcErrorAsync(context,
                "Bad Request: The specified Last-Event-ID is either invalid or expired.",
                StatusCodes.Status400BadRequest);
            return null;
        }

        return eventStreamReader;
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
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();
    }

    internal static string MakeNewSessionId()
    {
        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        return WebEncoders.Base64UrlEncode(buffer);
    }

    internal static async Task<JsonRpcMessage?> ReadJsonRpcMessageAsync(HttpContext context)
    {
        // Implementation for reading a JSON-RPC message from the request body
        var message = await context.Request.ReadFromJsonAsync(s_messageTypeInfo, context.RequestAborted);

        if (message is not null)
        {
            var protocolVersion = context.Request.Headers[McpProtocolVersionHeaderName].ToString();
            var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;

            if (isAuthenticated || !string.IsNullOrEmpty(protocolVersion))
            {
                message.Context ??= new();

                if (isAuthenticated)
                {
                    message.Context.User = context.User;
                }

                if (!string.IsNullOrEmpty(protocolVersion))
                {
                    message.Context.ProtocolVersion = protocolVersion;
                }
            }
        }

        return message;
    }

    internal static Task RunSessionAsync(HttpContext httpContext, McpServer session, CancellationToken requestAborted)
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

    internal static JsonTypeInfo<T> GetRequiredJsonTypeInfo<T>() => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    /// <summary>
    /// Validates the MCP-Protocol-Version header if present. A missing header is allowed for backwards compatibility,
    /// but an invalid or unsupported value must be rejected with 400 Bad Request per the MCP spec. Per SEP-2575, the
    /// rejection uses the <see cref="McpErrorCode.UnsupportedProtocolVersion"/> error code with a data payload
    /// listing the server's supported versions so the client can select a fallback.
    /// </summary>
    private static bool ValidateProtocolVersionHeader(HttpContext context, [NotNullWhen(false)] out JsonRpcErrorDetail? errorDetail)
    {
        var protocolVersionHeader = context.Request.Headers[McpProtocolVersionHeaderName].ToString();
        if (!string.IsNullOrEmpty(protocolVersionHeader) &&
            !s_supportedProtocolVersions.Contains(protocolVersionHeader))
        {
            errorDetail = new JsonRpcErrorDetail
            {
                Code = (int)McpErrorCode.UnsupportedProtocolVersion,
                Message = $"Bad Request: The MCP-Protocol-Version header value '{protocolVersionHeader}' is not supported.",
                Data = JsonSerializer.SerializeToNode(
                    new UnsupportedProtocolVersionErrorData
                    {
                        Supported = [.. s_supportedProtocolVersions],
                        Requested = protocolVersionHeader,
                    },
                    GetRequiredJsonTypeInfo<UnsupportedProtocolVersionErrorData>()),
            };
            return false;
        }

        errorDetail = null;
        return true;
    }

    /// <summary>
    /// Validates standard MCP request headers (Mcp-Method, Mcp-Name) and custom parameter headers
    /// (Mcp-Param-*) against the JSON-RPC request body.
    /// Validation is only performed for protocol versions that include the HTTP Standardization feature.
    /// </summary>
    /// <param name="context">The HTTP context containing the request headers.</param>
    /// <param name="message">The JSON-RPC message to validate against.</param>
    /// <param name="toolCollection">The tool collection to look up tool schemas for parameter header validation.</param>
    /// <param name="errorMessage">Set to the error message if validation fails; null otherwise.</param>
    /// <returns>True if validation passes; false otherwise.</returns>
    internal static bool ValidateMcpHeaders(HttpContext context, JsonRpcMessage message, McpServerPrimitiveCollection<McpServerTool>? toolCollection, [NotNullWhen(false)] out string? errorMessage)
    {
        // Only validate for protocol versions that support standard headers.
        var protocolVersion = context.Request.Headers[McpProtocolVersionHeaderName].ToString();
        if (!McpHttpHeaders.SupportsStandardHeaders(protocolVersion))
        {
            errorMessage = null;
            return true;
        }

        // Only validate for JSON-RPC requests and notifications, not responses.
        if (!(message is JsonRpcRequest || message is JsonRpcNotification))
        {
            errorMessage = null;
            return true;
        }

        // For requests that support standard headers, the Mcp-Method header must be present
        // and match the method in the JSON-RPC body.
        if (!context.Request.Headers.ContainsKey(McpHttpHeaders.Method))
        {
            errorMessage = "Missing required Mcp-Method header.";
            return false;
        }

        var mcpMethodInHeader = context.Request.Headers[McpHttpHeaders.Method].ToString().Trim();
        var mcpMethodInBody = message switch
        {
            JsonRpcRequest request => request.Method,
            JsonRpcNotification notification => notification.Method,
            _ => null, // This case is already ruled out by the earlier check, but we need it to satisfy the compiler.
        };

        if (!string.Equals(mcpMethodInHeader, mcpMethodInBody, StringComparison.Ordinal))
        {
            errorMessage = $"Header mismatch: Mcp-Method header value '{mcpMethodInHeader}' does not match body value '{mcpMethodInBody}'.";
            return false;
        }

        // From here on, only validate resources/read, tools/call, and prompts/get requests
        if (mcpMethodInBody is not (RequestMethods.ToolsCall or RequestMethods.ResourcesRead or RequestMethods.PromptsGet))
        {
            errorMessage = null;
            return true;
        }

        // For these requests, the Mcp-Name header must be present and match the name or uri in the JSON-RPC body.
        if (!context.Request.Headers.ContainsKey(McpHttpHeaders.Name))
        {
            errorMessage = "Missing required Mcp-Name header.";
            return false;
        }

        var mcpNameInHeader = context.Request.Headers[McpHttpHeaders.Name].ToString().Trim();

        // Per SEP-2243, non-ASCII Mcp-Name values MUST be Base64-encoded using the
        // "=?base64?...?=" wrapper. Reject raw values containing characters outside the valid
        // HTTP header value range, then decode so the comparison below is against the
        // decoded value (mirrors the Mcp-Param-* validation in ValidateCustomParamHeaders).
        if (!IsValidHeaderValue(mcpNameInHeader))
        {
            errorMessage = "Header mismatch: Mcp-Name header contains invalid characters.";
            return false;
        }

        var decodedMcpNameInHeader = McpHeaderEncoder.DecodeValue(mcpNameInHeader);
        if (decodedMcpNameInHeader is null)
        {
            errorMessage = "Header mismatch: Mcp-Name header contains invalid Base64 encoding.";
            return false;
        }

        // Extract the params and name value from the body based on the method, if present.
        var bodyParams = message switch
        {
            JsonRpcRequest request => request.Params,
            JsonRpcNotification notification => notification.Params,
            _ => null,
        };
        var mcpNameInBody = mcpMethodInBody switch
        {
            RequestMethods.ToolsCall => GetJsonNodeStringProperty(bodyParams, "name"),
            RequestMethods.ResourcesRead => GetJsonNodeStringProperty(bodyParams, "uri"),
            RequestMethods.PromptsGet => GetJsonNodeStringProperty(bodyParams, "name"),
            _ => null,
        };

        // Check that the header value matches the body value if the body value is present.
        if (!string.Equals(decodedMcpNameInHeader, mcpNameInBody, StringComparison.Ordinal))
        {
            errorMessage = $"Header mismatch: Mcp-Name header value '{mcpNameInHeader}' does not match body value '{mcpNameInBody}'.";
            return false;
        }

        // Validate Mcp-Param-* custom headers against tool schema
        if (!ValidateCustomParamHeaders(context, message, toolCollection, out errorMessage))
        {
            return false;
        }

        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Validates that all parameters annotated with <c>x-mcp-header</c> in the tool's input schema
    /// have corresponding <c>Mcp-Param-*</c> headers present in the request, and that any present
    /// <c>Mcp-Param-*</c> headers have valid encoding.
    /// </summary>
    private static bool ValidateCustomParamHeaders(
        HttpContext context,
        JsonRpcMessage message,
        McpServerPrimitiveCollection<McpServerTool>? toolCollection,
        [NotNullWhen(false)] out string? errorMessage)
    {
        // Custom param headers are only relevant for tools/call requests
        if (message is not JsonRpcRequest { Method: RequestMethods.ToolsCall, Params: { } bodyParams })
        {
            errorMessage = null;
            return true;
        }

        // Look up the tool to check for x-mcp-header annotations in the schema
        var toolName = GetJsonNodeStringProperty(bodyParams, "name");
        if (toolName is null || toolCollection is null || !toolCollection.TryGetPrimitive(toolName, out var tool))
        {
            errorMessage = null;
            return true;
        }

        var inputSchema = tool.ProtocolTool.InputSchema;
        if (inputSchema.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !inputSchema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            errorMessage = null;
            return true;
        }

        // Get the arguments from the body for value comparison
        System.Text.Json.Nodes.JsonNode? arguments = null;
        if (bodyParams is System.Text.Json.Nodes.JsonObject paramsObj)
        {
            paramsObj.TryGetPropertyValue("arguments", out arguments);
        }

        // Check that every x-mcp-header annotated parameter has a corresponding header,
        // that the header value is validly encoded, and that it matches the body value.
        return ValidateCustomParamHeadersFromProperties(context, properties, arguments, out errorMessage);
    }

    /// <summary>
    /// Recursively validates x-mcp-header annotated properties at any nesting depth.
    /// </summary>
    private static bool ValidateCustomParamHeadersFromProperties(
        HttpContext context,
        System.Text.Json.JsonElement properties,
        System.Text.Json.Nodes.JsonNode? arguments,
        [NotNullWhen(false)] out string? errorMessage)
    {
        foreach (var property in properties.EnumerateObject())
        {
            if (property.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                continue;
            }

            // Recurse into nested object properties
            if (property.Value.TryGetProperty("properties", out var nestedProperties) &&
                nestedProperties.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                System.Text.Json.Nodes.JsonNode? nestedArgs = null;
                if (arguments is System.Text.Json.Nodes.JsonObject parentObj)
                {
                    parentObj.TryGetPropertyValue(property.Name, out nestedArgs);
                }

                if (!ValidateCustomParamHeadersFromProperties(context, nestedProperties, nestedArgs, out errorMessage))
                {
                    return false;
                }
            }

            if (!property.Value.TryGetProperty("x-mcp-header", out var headerNameElement))
            {
                continue;
            }

            var headerName = headerNameElement.GetString();
            if (string.IsNullOrEmpty(headerName))
            {
                continue;
            }

            var fullHeaderName = $"{McpHttpHeaders.ParamPrefix}{headerName}";
            if (!context.Request.Headers.ContainsKey(fullHeaderName))
            {
                // Per the SEP: if the parameter value is null or not provided in
                // the arguments, the client MUST omit the header and the server
                // MUST NOT expect it. Only reject when a non-null value is present
                // in the body but the header is missing.
                bool hasNonNullBodyValue = arguments is System.Text.Json.Nodes.JsonObject argsForMissing &&
                    argsForMissing.TryGetPropertyValue(property.Name, out var argForMissing) &&
                    argForMissing is not null &&
                    argForMissing.GetValueKind() != System.Text.Json.JsonValueKind.Null;

                if (hasNonNullBodyValue)
                {
                    errorMessage = $"Missing required {fullHeaderName} header for parameter '{property.Name}' annotated with x-mcp-header.";
                    return false;
                }

                continue;
            }

            var actualHeaderValue = context.Request.Headers[fullHeaderName].ToString().Trim();

            // Validate the raw header value for invalid characters per SEP.
            // Servers MUST reject headers containing characters outside the valid HTTP header value range.
            if (!IsValidHeaderValue(actualHeaderValue))
            {
                errorMessage = $"Header mismatch: {fullHeaderName} header contains invalid characters.";
                return false;
            }

            var decodedActual = McpHeaderEncoder.DecodeValue(actualHeaderValue);
            if (decodedActual is null)
            {
                errorMessage = $"Header mismatch: {fullHeaderName} header contains invalid Base64 encoding.";
                return false;
            }

            // Verify the header value matches the argument value in the body
            if (arguments is System.Text.Json.Nodes.JsonObject argsObj &&
                argsObj.TryGetPropertyValue(property.Name, out var argNode) &&
                argNode is not null)
            {
                var expectedHeaderValue = McpHeaderEncoder.ConvertToHeaderValue(argNode);
                if (expectedHeaderValue is not null)
                {
                    var decodedExpected = McpHeaderEncoder.DecodeValue(expectedHeaderValue);
                    switch (ValuesMatch(decodedActual, decodedExpected, property.Value))
                    {
                        case HeaderValueComparison.IntegerOutOfRange:
                            errorMessage = $"Header mismatch: {fullHeaderName} integer value for parameter '{property.Name}' is outside the JavaScript safe integer range (-{MaxSafeInteger} to {MaxSafeInteger}).";
                            return false;
                        case HeaderValueComparison.Mismatch:
                            errorMessage = $"Header mismatch: {fullHeaderName} header value does not match body argument '{property.Name}'.";
                            return false;
                    }
                }
            }
        }

        errorMessage = null;
        return true;
    }

    private static string? GetJsonNodeStringProperty(System.Text.Json.Nodes.JsonNode? node, string propertyName)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj && obj.TryGetPropertyValue(propertyName, out var value))
        {
            return value?.GetValue<string>();
        }

        return null;
    }

    // Valid HTTP header field-value characters per RFC 9110: horizontal tab (0x09),
    // space (0x20), and visible ASCII (0x21-0x7E).
    private static readonly SearchValues<char> s_validHeaderValueChars =
        SearchValues.Create("\t !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~");

    /// <summary>
    /// Validates that a header value contains only characters allowed in HTTP header field values
    /// per RFC 9110: visible ASCII (0x21-0x7E), space (0x20), and horizontal tab (0x09).
    /// </summary>
    private static bool IsValidHeaderValue(string value) =>
        value.AsSpan().IndexOfAnyExcept(s_validHeaderValueChars) < 0;

    /// <summary>
    /// The maximum magnitude for an integer that can be represented exactly by an IEEE 754
    /// double-precision value (2^53 - 1). Per SEP-2243 integer x-mcp-header values MUST be within
    /// the JavaScript safe integer range (-2^53+1 to 2^53-1).
    /// </summary>
    private const long MaxSafeInteger = 9007199254740991L;

    private enum HeaderValueComparison
    {
        Match,
        Mismatch,
        IntegerOutOfRange,
    }

    /// <summary>
    /// Compares two decoded header values. For <c>integer</c>-typed parameters the values are
    /// compared numerically (so cross-SDK forms such as <c>"42"</c> and <c>"42.0"</c> are treated
    /// as equal) and validated against the JavaScript safe integer range per SEP-2243.
    /// </summary>
    private static HeaderValueComparison ValuesMatch(string? actual, string? expected, System.Text.Json.JsonElement propertySchema)
    {
        // Per SEP-2243, x-mcp-header may only be applied to integer, string, or boolean parameters.
        // For "integer" the spec recommends numeric comparison so that representations like "42" and
        // "42.0" are considered equal, while still requiring values to stay within the safe range.
        // This must run before the ordinal comparison below so that an invalid integer value is
        // rejected even when the header and body strings are byte-for-byte identical.
        if (actual is not null && expected is not null && SchemaTypeIsInteger(propertySchema))
        {
            var actualResult = ParseSafeInteger(actual, out long actualValue);
            var expectedResult = ParseSafeInteger(expected, out long expectedValue);

            // A numeric value outside the safe integer range is always rejected.
            if (actualResult == SafeIntegerParse.OutOfRange || expectedResult == SafeIntegerParse.OutOfRange)
            {
                return HeaderValueComparison.IntegerOutOfRange;
            }

            if (actualResult == SafeIntegerParse.SafeInteger && expectedResult == SafeIntegerParse.SafeInteger)
            {
                return actualValue == expectedValue ? HeaderValueComparison.Match : HeaderValueComparison.Mismatch;
            }

            // A numeric-but-non-integer value (e.g. "42.5") for an integer-typed parameter is invalid
            // and must not be allowed to slip through the ordinal comparison just because the header
            // and body strings happen to be identical.
            if (actualResult == SafeIntegerParse.NonInteger || expectedResult == SafeIntegerParse.NonInteger)
            {
                return HeaderValueComparison.Mismatch;
            }

            // Otherwise at least one side is not numeric at all (NotNumeric); fall through to the
            // ordinal comparison below.
        }

        return string.Equals(actual, expected, StringComparison.Ordinal)
            ? HeaderValueComparison.Match
            : HeaderValueComparison.Mismatch;
    }

    /// <summary>
    /// Determines whether the property schema's <c>type</c> keyword declares an <c>integer</c> type,
    /// either directly or as a member of a JSON Schema union array (e.g. <c>["integer", "null"]</c>).
    /// </summary>
    private static bool SchemaTypeIsInteger(System.Text.Json.JsonElement propertySchema)
    {
        if (!propertySchema.TryGetProperty("type", out var typeElement))
        {
            return false;
        }

        switch (typeElement.ValueKind)
        {
            case System.Text.Json.JsonValueKind.String:
                return typeElement.ValueEquals("integer");

            case System.Text.Json.JsonValueKind.Array:
                foreach (var entry in typeElement.EnumerateArray())
                {
                    if (entry.ValueKind == System.Text.Json.JsonValueKind.String && entry.ValueEquals("integer"))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Classifies how a header/body string parses as a SEP-2243 integer value.
    /// </summary>
    private enum SafeIntegerParse
    {
        /// <summary>A whole number within the JavaScript safe integer range.</summary>
        SafeInteger,

        /// <summary>A numeric value whose magnitude is outside the safe integer range.</summary>
        OutOfRange,

        /// <summary>A numeric value that is not a whole number (e.g. <c>"42.5"</c>).</summary>
        NonInteger,

        /// <summary>The value is not a numeric literal at all.</summary>
        NotNumeric,
    }

    /// <summary>
    /// Parses a header/body value as a whole integer within the JavaScript safe integer range.
    /// Decimal and exponent forms whose fractional part is zero (e.g. <c>"42.0"</c>, <c>"4.2e1"</c>)
    /// are accepted. <see cref="long.TryParse(string, System.Globalization.NumberStyles, IFormatProvider, out long)"/>
    /// inspects the actual digits (so it rejects non-integers such as <c>"42.5"</c> without rounding)
    /// and fails fast on overflow (so a huge literal such as <c>"1e1000000"</c> cannot allocate a
    /// large number).
    /// </summary>
    private static SafeIntegerParse ParseSafeInteger(string text, out long value)
    {
        value = 0;

        const System.Globalization.NumberStyles Styles =
            System.Globalization.NumberStyles.AllowLeadingSign |
            System.Globalization.NumberStyles.AllowDecimalPoint |
            System.Globalization.NumberStyles.AllowExponent;

        if (long.TryParse(text, Styles, System.Globalization.CultureInfo.InvariantCulture, out long parsed))
        {
            if (parsed < -MaxSafeInteger || parsed > MaxSafeInteger)
            {
                return SafeIntegerParse.OutOfRange;
            }

            value = parsed;
            return SafeIntegerParse.SafeInteger;
        }

        // The value is not representable as a 64-bit integer. Use double only as an order-of-magnitude
        // gate to distinguish a numeric literal beyond the safe range (e.g. "1e100") from a numeric but
        // non-integer value (e.g. "42.5"). double's loss of precision is irrelevant for this magnitude
        // comparison because every in-range value was already handled exactly by long.TryParse above.
        if (double.TryParse(text, Styles, System.Globalization.CultureInfo.InvariantCulture, out double d))
        {
            return System.Math.Abs(d) > MaxSafeInteger ? SafeIntegerParse.OutOfRange : SafeIntegerParse.NonInteger;
        }

        return SafeIntegerParse.NotNumeric;
    }

    private static Task WriteJsonRpcErrorDetailAsync(HttpContext context, JsonRpcErrorDetail detail, int statusCode)
    {
        var jsonRpcError = new JsonRpcError { Error = detail };
        return Results.Json(jsonRpcError, s_errorTypeInfo, statusCode: statusCode).ExecuteAsync(context);
    }

    private static bool MatchesApplicationJsonMediaType(MediaTypeHeaderValue acceptHeaderValue)
        => acceptHeaderValue.MatchesMediaType("application/json");

    private static bool MatchesTextEventStreamMediaType(MediaTypeHeaderValue acceptHeaderValue)
        => acceptHeaderValue.MatchesMediaType("text/event-stream");
}
