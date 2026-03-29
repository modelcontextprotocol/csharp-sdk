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
using System.Security.Claims;
using System.Security.Cryptography;
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
        "DRAFT-2026-v1",
    ];

    private static readonly JsonTypeInfo<JsonRpcMessage> s_messageTypeInfo = GetRequiredJsonTypeInfo<JsonRpcMessage>();
    private static readonly JsonTypeInfo<JsonRpcError> s_errorTypeInfo = GetRequiredJsonTypeInfo<JsonRpcError>();

    private static bool AllowNewSessionForNonInitializeRequests { get; } =
        AppContext.TryGetSwitch("ModelContextProtocol.AspNetCore.AllowNewSessionForNonInitializeRequests", out var enabled) && enabled;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _migrationLocks = new(StringComparer.Ordinal);

    public HttpServerTransportOptions HttpServerTransportOptions => httpServerTransportOptions.Value;

    public async Task HandlePostRequestAsync(HttpContext context)
    {
        if (!ValidateProtocolVersionHeader(context, out var errorMessage))
        {
            await WriteJsonRpcErrorAsync(context, errorMessage!, StatusCodes.Status400BadRequest);
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

        if (!ValidateMcpHeaders(context, message, mcpServerOptionsSnapshot.Value.ToolCollection, out errorMessage))
        {
            await WriteJsonRpcErrorAsync(context, errorMessage!, StatusCodes.Status400BadRequest, (int)McpErrorCode.HeaderMismatch);
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
        if (!ValidateProtocolVersionHeader(context, out var errorMessage))
        {
            await WriteJsonRpcErrorAsync(context, errorMessage!, StatusCodes.Status400BadRequest);
            return;
        }

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
        if (!ValidateProtocolVersionHeader(context, out var errorMessage))
        {
            await WriteJsonRpcErrorAsync(context, errorMessage!, StatusCodes.Status400BadRequest);
            return;
        }

        var sessionId = context.Request.Headers[McpSessionIdHeaderName].ToString();
        if (sessionManager.TryRemove(sessionId, out var session))
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
        if (HttpServerTransportOptions.SessionMigrationHandler is not { } handler)
        {
            return null;
        }

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

    private async ValueTask<StreamableHttpSession> StartNewSessionAsync(HttpContext context)
    {
        string sessionId;
        StreamableHttpServerTransport transport;

        if (!HttpServerTransportOptions.Stateless)
        {
            sessionId = MakeNewSessionId();
            transport = new(loggerFactory)
            {
                SessionId = sessionId,
                FlowExecutionContextFromRequests = !HttpServerTransportOptions.PerSessionExecutionContext,
                EventStreamStore = HttpServerTransportOptions.EventStreamStore,
                OnSessionInitialized = HttpServerTransportOptions.SessionMigrationHandler is { } handler
                    ? (initParams, ct) => handler.OnSessionInitializedAsync(context, sessionId, initParams, ct)
                    : null,
            };

            context.Response.Headers[McpSessionIdHeaderName] = sessionId;
        }
        else
        {
            // In stateless mode, each request is independent. Don't set any session ID on the transport.
            // If in the future we support resuming stateless requests, we should populate
            // the event stream store and retry interval here as well.
            sessionId = "";
            transport = new(loggerFactory)
            {
                Stateless = true,
            };
        }

        return await CreateSessionAsync(context, transport, sessionId);
    }

    private async ValueTask<StreamableHttpSession> CreateSessionAsync(
        HttpContext context,
        StreamableHttpServerTransport transport,
        string sessionId,
        Action<McpServerOptions>? configureOptions = null)
    {
        var mcpServerServices = applicationServices;
        var mcpServerOptions = mcpServerOptionsSnapshot.Value;
        if (HttpServerTransportOptions.Stateless || HttpServerTransportOptions.ConfigureSessionOptions is not null || configureOptions is not null)
        {
            mcpServerOptions = mcpServerOptionsFactory.Create(Options.DefaultName);

            if (HttpServerTransportOptions.Stateless)
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
            FlowExecutionContextFromRequests = !HttpServerTransportOptions.PerSessionExecutionContext,
            EventStreamStore = HttpServerTransportOptions.EventStreamStore,
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
        if (HttpServerTransportOptions.EventStreamStore is not { } eventStreamStore)
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

        if (context.User?.Identity?.IsAuthenticated == true && message is not null)
        {
            message.Context = new()
            {
                User = context.User,
            };
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
    /// but an invalid or unsupported value must be rejected with 400 Bad Request per the MCP spec.
    /// </summary>
    private static bool ValidateProtocolVersionHeader(HttpContext context, out string? errorMessage)
    {
        var protocolVersionHeader = context.Request.Headers[McpProtocolVersionHeaderName].ToString();
        if (!string.IsNullOrEmpty(protocolVersionHeader) &&
            !s_supportedProtocolVersions.Contains(protocolVersionHeader))
        {
            errorMessage = $"Bad Request: The MCP-Protocol-Version header value '{protocolVersionHeader}' is not supported.";
            return false;
        }

        errorMessage = null;
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
    internal static bool ValidateMcpHeaders(HttpContext context, JsonRpcMessage message, McpServerPrimitiveCollection<McpServerTool>? toolCollection, out string? errorMessage)
    {
        // Only validate for protocol versions that support standard headers.
        var protocolVersion = context.Request.Headers[McpProtocolVersionHeaderName].ToString();
        if (string.IsNullOrEmpty(protocolVersion) ||
            string.Compare(protocolVersion, McpHttpHeaders.MinVersionForStandardHeaders, StringComparison.Ordinal) < 0)
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

        var mcpMethodInHeader = context.Request.Headers[McpHttpHeaders.Method].ToString();
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

        // From here on, only validate tools/read, tools/call, and prompts/get requests
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

        var mcpNameInHeader = context.Request.Headers[McpHttpHeaders.Name].ToString();

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
        if (!string.Equals(mcpNameInHeader, mcpNameInBody, StringComparison.Ordinal))
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
        out string? errorMessage)
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
        foreach (var property in properties.EnumerateObject())
        {
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

            var actualHeaderValue = context.Request.Headers[fullHeaderName].ToString();
            if (string.IsNullOrEmpty(actualHeaderValue))
            {
                continue;
            }

            var decodedActual = Client.McpHeaderEncoder.DecodeValue(actualHeaderValue);
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
                var expectedHeaderValue = ConvertJsonNodeToHeaderValue(argNode);
                if (expectedHeaderValue is not null)
                {
                    var decodedExpected = Client.McpHeaderEncoder.DecodeValue(expectedHeaderValue);
                    if (!string.Equals(decodedActual, decodedExpected, StringComparison.Ordinal))
                    {
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

    private static string? ConvertJsonNodeToHeaderValue(System.Text.Json.Nodes.JsonNode node)
    {
        if (node is not System.Text.Json.Nodes.JsonValue jsonValue)
        {
            return null;
        }

        object? value = jsonValue.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.String => jsonValue.GetValue<string>(),
            System.Text.Json.JsonValueKind.Number => jsonValue.GetValue<double>(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => null
        };

        return Client.McpHeaderEncoder.EncodeValue(value);
    }

    private static bool MatchesApplicationJsonMediaType(MediaTypeHeaderValue acceptHeaderValue)
        => acceptHeaderValue.MatchesMediaType("application/json");

    private static bool MatchesTextEventStreamMediaType(MediaTypeHeaderValue acceptHeaderValue)
        => acceptHeaderValue.MatchesMediaType("text/event-stream");
}
