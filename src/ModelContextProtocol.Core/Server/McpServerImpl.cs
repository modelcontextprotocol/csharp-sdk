using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Server;

/// <inheritdoc />
#pragma warning disable MCPEXP001, MCPEXP002
internal sealed partial class McpServerImpl : McpServer
{
    internal static Implementation DefaultImplementation { get; } = new()
    {
        Name = AssemblyNameHelper.DefaultAssemblyName.Name ?? nameof(McpServer),
        Version = AssemblyNameHelper.DefaultAssemblyName.Version?.ToString() ?? "1.0.0",
    };

    private readonly ILogger _logger;
    private readonly ITransport _sessionTransport;
    private readonly bool _servicesScopePerRequest;
    private readonly List<Action> _disposables = [];
    private readonly NotificationHandlers _notificationHandlers;
    private readonly RequestHandlers _requestHandlers;
    private readonly McpSessionHandler _sessionHandler;
    private readonly string[] _supportedProtocolVersions;
    private readonly string[] _initializeHandshakeProtocolVersions;
    private readonly string[] _perRequestMetadataProtocolVersions;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _taskCancellationSources = new();
    private readonly ConcurrentDictionary<string, MrtrContinuation> _mrtrContinuations = new();
    private readonly ConcurrentDictionary<RequestId, MrtrContext> _mrtrContextsByRequestId = new();

    private static readonly string[] s_perRequestMetadataKeys =
    [
        MetaKeys.ProtocolVersion,
        MetaKeys.ClientInfo,
        MetaKeys.ClientCapabilities,
        MetaKeys.LogLevel,
    ];

    // Track MRTR handler tasks using the same inFlightCount + TCS pattern as
    // McpSessionHandler.ProcessMessagesCoreAsync. Starts at 1 for DisposeAsync itself.
    private int _mrtrInFlightCount = 1;
    private readonly TaskCompletionSource<bool> _allMrtrHandlersCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ClientCapabilities? _clientCapabilities;
    private Implementation? _clientInfo;

    private readonly string _serverOnlyEndpointName;
    private string? _negotiatedProtocolVersion;
    private string _endpointName;
    private int _started;

    private bool _disposed;

    /// <summary>Holds a boxed <see cref="LoggingLevel"/> value for the server.</summary>
    /// <remarks>
    /// Initialized to non-null the first time SetLevel is used. This is stored as a strong box
    /// rather than a nullable to be able to manipulate it atomically.
    /// </remarks>
    private StrongBox<LoggingLevel>? _loggingLevel;

    /// <summary>
    /// Creates a new instance of <see cref="McpServerImpl"/>.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established session.</param>
    /// <param name="options">Configuration options for this server, including capabilities.
    /// Make sure to accurately reflect exactly what capabilities the server supports and does not support.</param>
    /// <param name="loggerFactory">Logger factory to use for logging</param>
    /// <param name="serviceProvider">Optional service provider to use for dependency injection</param>
    /// <exception cref="McpException">The server was incorrectly configured.</exception>
    public McpServerImpl(ITransport transport, McpServerOptions options, ILoggerFactory? loggerFactory, IServiceProvider? serviceProvider)
#pragma warning restore MCPEXP002
    {
        Throw.IfNull(transport);
        Throw.IfNull(options);

        _sessionTransport = transport;
        ServerOptions = options;
        Services = serviceProvider;
        _supportedProtocolVersions = GetConfiguredSupportedProtocolVersions(options.ProtocolVersion);
        _initializeHandshakeProtocolVersions = [.. _supportedProtocolVersions.Where(McpProtocolVersions.SupportsInitializeHandshake)];
        _perRequestMetadataProtocolVersions = [.. _supportedProtocolVersions.Where(McpProtocolVersions.RequiresPerRequestMetadata)];
        _serverOnlyEndpointName = $"Server ({options.ServerInfo?.Name ?? DefaultImplementation.Name} {options.ServerInfo?.Version ?? DefaultImplementation.Version})";
        _endpointName = _serverOnlyEndpointName;
        _servicesScopePerRequest = options.ScopeRequests;
        _logger = loggerFactory?.CreateLogger<McpServer>() ?? NullLogger<McpServer>.Instance;

        _clientInfo = options.KnownClientInfo;
        _clientCapabilities = options.KnownClientCapabilities;
        UpdateEndpointNameWithClientInfo();

        _notificationHandlers = new();
        _requestHandlers = [];

        // Configure all request handlers based on the supplied options.
        ServerCapabilities = new();
        ConfigureInitialize(options);
        ConfigureDiscover(options);
        ConfigureTools(options);
        ConfigurePrompts(options);
        ConfigureResources(options);
        ConfigureLogging(options);
        ConfigureCompletion(options);
        ConfigureSubscriptions(options);
        ConfigureExperimentalAndExtensions(options);
        ConfigureTasks(options);
        ConfigureMrtr();

        // Register any notification handlers that were provided.
        if (options.Handlers.NotificationHandlers is { } notificationHandlers)
        {
            _notificationHandlers.RegisterRange(notificationHandlers);
        }

        // A stateful session can push unsolicited list-changed notifications, so subscribe to the
        // collection change events. A stateless HTTP server cannot send unsolicited notifications, so
        // instead suppress the listChanged capability it would otherwise advertise.
        if (HasStatefulTransport())
        {
            Register(ServerOptions.ToolCollection, NotificationMethods.ToolListChangedNotification);
            Register(ServerOptions.PromptCollection, NotificationMethods.PromptListChangedNotification);
            Register(ServerOptions.ResourceCollection, NotificationMethods.ResourceListChangedNotification);

            void Register<TPrimitive>(McpServerPrimitiveCollection<TPrimitive>? collection, string notificationMethod)
                where TPrimitive : IMcpServerPrimitive
            {
                if (collection is not null)
                {
                    EventHandler changed = (sender, e) => _ = SendListChangedNotificationAsync(notificationMethod);
                    collection.Changed += changed;
                    _disposables.Add(() => collection.Changed -= changed);
                }
            }
        }
        else
        {
            if (ServerCapabilities.Tools is not null)
                ServerCapabilities.Tools.ListChanged = null;
            if (ServerCapabilities.Prompts is not null)
                ServerCapabilities.Prompts.ListChanged = null;
            if (ServerCapabilities.Resources is not null)
                ServerCapabilities.Resources.ListChanged = null;
        }

        // And initialize the session. The built-in meta-reading filter runs ahead of any
        // user-supplied incoming filters; see PrependMetaReadingFilter for what it records and why.
        var incomingMessageFilter = PrependMetaReadingFilter(BuildMessageFilterPipeline(options.Filters.Message.IncomingFilters));
        var outgoingMessageFilter = BuildMessageFilterPipeline(options.Filters.Message.OutgoingFilters);

        _sessionHandler = new McpSessionHandler(
            isServer: true,
            _sessionTransport,
            _endpointName!,
            _requestHandlers,
            _notificationHandlers,
            incomingMessageFilter,
            outgoingMessageFilter,
            _logger);
    }

    /// <summary>
    /// Wraps <paramref name="inner"/> so that, for every JSON-RPC request, a built-in filter first
    /// synchronizes server-side state (<see cref="_negotiatedProtocolVersion"/>, <see cref="_clientInfo"/>)
    /// from the per-request <c>_meta</c> values projected onto <see cref="JsonRpcMessageContext"/> and
    /// validates the per-request protocol version, before delegating to the user-supplied incoming filters.
    /// </summary>
    /// <remarks>
    /// Under the 2026-07-28 protocol revision (SEP-2575) there is no <c>initialize</c> handshake, so these values
    /// MUST be populated per-request. Per-request client capabilities and client info are consumed request-scoped
    /// by <see cref="DestinationBoundMcpServer"/> and are not read from server-wide state by request handlers. The
    /// shared <see cref="_clientInfo"/> write below is best-effort and used only to derive the session endpoint
    /// name for logging/telemetry. For initialize-handshake clients the per-request values are absent and the built-in
    /// filter is a no-op (the values were captured during the initialize handler).
    /// </remarks>
    private JsonRpcMessageFilter PrependMetaReadingFilter(JsonRpcMessageFilter inner)
    {
        JsonRpcMessageFilter metaReadingFilter = next => async (message, cancellationToken) =>
        {
            if (message is JsonRpcRequest { Method: RequestMethods.Initialize } initializeRequest)
            {
                ValidateInitializeRequestBoundary(initializeRequest);
            }
            else if (message is JsonRpcRequest request)
            {
                var context = request.Context;
                bool endpointNameNeedsRefresh = false;
                bool hasProtocolVersionMeta = HasMetaKey(request, MetaKeys.ProtocolVersion);
                bool hasReservedPerRequestMeta = TryGetPerRequestMetadataKey(request, out var reservedPerRequestMetaKey);

                if (context?.ProtocolVersion is { } protocolVersion)
                {
                    bool protocolVersionAlreadyEstablished = _negotiatedProtocolVersion is not null;
                    if (protocolVersionAlreadyEstablished)
                    {
                        SetNegotiatedProtocolVersion(protocolVersion);
                    }

                    // Per SEP-2575, the server MUST reject any request whose per-request
                    // _meta/io.modelcontextprotocol/protocolVersion is not one of its supported versions
                    // with an UnsupportedProtocolVersionError (-32022) carrying the supported list.
                    if (!_supportedProtocolVersions.Contains(protocolVersion))
                    {
                        throw new UnsupportedProtocolVersionException(
                            requested: protocolVersion,
                            supported: _supportedProtocolVersions);
                    }

                    if (McpProtocolVersions.RequiresPerRequestMetadata(protocolVersion))
                    {
                        ValidateRequiredPerRequestMetadata(
                            protocolVersion,
                            hasProtocolVersionMeta,
                            context.ClientInfo is not null,
                            context.ClientCapabilities is not null);
                    }
                    else if (McpProtocolVersions.SupportsInitializeHandshake(protocolVersion))
                    {
                        if (_negotiatedProtocolVersion is null && hasProtocolVersionMeta)
                        {
                            throw new UnsupportedProtocolVersionException(
                                requested: protocolVersion,
                                supported: _perRequestMetadataProtocolVersions,
                                message: $"Protocol version '{protocolVersion}' requires the initialize handshake and cannot be selected through per-request metadata.");
                        }

                        if (hasReservedPerRequestMeta)
                        {
                            ThrowReservedPerRequestMetadata(requestedProtocolVersion: protocolVersion, reservedPerRequestMetaKey);
                        }
                    }

                    if (!protocolVersionAlreadyEstablished)
                    {
                        SetNegotiatedProtocolVersion(protocolVersion);
                    }
                }
                else if (_negotiatedProtocolVersion is null)
                {
                    if (request.Method == RequestMethods.ServerDiscover)
                    {
                        throw new McpProtocolException(
                            $"The '{RequestMethods.ServerDiscover}' request requires per-request metadata declaring a supported protocol version.",
                            McpErrorCode.InvalidParams);
                    }

                    if (hasReservedPerRequestMeta)
                    {
                        ThrowReservedPerRequestMetadata(requestedProtocolVersion: null, reservedPerRequestMetaKey);
                    }
                }
                else if (McpProtocolVersions.SupportsInitializeHandshake(_negotiatedProtocolVersion) && hasReservedPerRequestMeta)
                {
                    ThrowReservedPerRequestMetadata(_negotiatedProtocolVersion, reservedPerRequestMetaKey);
                }

                ValidateRequestMethodBoundary(request);

                if (context?.ClientInfo is { } clientInfo &&
                    (_clientInfo is null || !string.Equals(_clientInfo.Name, clientInfo.Name, StringComparison.Ordinal) ||
                     !string.Equals(_clientInfo.Version, clientInfo.Version, StringComparison.Ordinal)))
                {
                    // This shared write is best-effort and used only to derive the session endpoint name for
                    // logging/telemetry. It is intentionally NOT read by request handlers on 2026-07-28+ sessions:
                    // DestinationBoundMcpServer resolves ClientInfo (and ClientCapabilities) request-scoped from
                    // the per-request _meta so concurrent requests never observe each other's values. Under a
                    // draft stateful session with differing per-request client info, the last writer wins here,
                    // which only affects the logged endpoint name and never the request-scoped values handlers see.
                    _clientInfo = clientInfo;
                    endpointNameNeedsRefresh = true;
                }

                if (endpointNameNeedsRefresh)
                {
                    UpdateEndpointNameWithClientInfo();
                    _sessionHandler.EndpointName = _endpointName;
                }
            }
            else if (message is JsonRpcNotification notification)
            {
                ValidateNotificationBoundary(notification);
            }

            await next(message, cancellationToken).ConfigureAwait(false);
        };

        return next => metaReadingFilter(inner(next));
    }

    private static void ValidateRequiredPerRequestMetadata(
        string protocolVersion,
        bool hasProtocolVersionMeta,
        bool hasClientInfoMeta,
        bool hasClientCapabilitiesMeta)
    {
        if (!hasProtocolVersionMeta)
        {
            ThrowMissingPerRequestMetadata(protocolVersion, MetaKeys.ProtocolVersion);
        }

        if (!hasClientInfoMeta)
        {
            ThrowMissingPerRequestMetadata(protocolVersion, MetaKeys.ClientInfo);
        }

        if (!hasClientCapabilitiesMeta)
        {
            ThrowMissingPerRequestMetadata(protocolVersion, MetaKeys.ClientCapabilities);
        }
    }

    private static void ThrowMissingPerRequestMetadata(string protocolVersion, string key) =>
        throw new McpProtocolException(
            $"Requests using protocol version '{protocolVersion}' must include '_meta/{key}'.",
            McpErrorCode.InvalidParams);

    private static void ThrowReservedPerRequestMetadata(string? requestedProtocolVersion, string key) =>
        throw new McpProtocolException(
            requestedProtocolVersion is null
                ? $"The reserved per-request metadata key '_meta/{key}' requires a protocol version that uses per-request metadata."
                : $"The reserved per-request metadata key '_meta/{key}' is not valid with protocol version '{requestedProtocolVersion}'.",
            McpErrorCode.InvalidRequest);

    private static bool TryGetPerRequestMetadataKey(JsonRpcRequest request, out string key)
    {
        foreach (var candidate in s_perRequestMetadataKeys)
        {
            if (HasMetaKey(request, candidate))
            {
                key = candidate;
                return true;
            }
        }

        key = "";
        return false;
    }

    private static bool HasMetaKey(JsonRpcRequest request, string key) =>
        request.Params is JsonObject paramsObj &&
        paramsObj["_meta"] is JsonObject metaObj &&
        metaObj.ContainsKey(key);

    private void ValidateInitializeRequestBoundary(JsonRpcRequest request)
    {
        if (request.Context?.ProtocolVersion is { } protocolVersion &&
            !McpProtocolVersions.SupportsInitializeHandshake(protocolVersion))
        {
            throw new UnsupportedProtocolVersionException(
                requested: protocolVersion,
                supported: _initializeHandshakeProtocolVersions,
                message: $"Protocol version '{protocolVersion}' is not available through the initialize handshake.");
        }

        if (TryGetPerRequestMetadataKey(request, out var key))
        {
            ThrowReservedPerRequestMetadata(TryGetStringParam(request, "protocolVersion"), key);
        }
    }

    private static string? TryGetStringParam(JsonRpcRequest request, string propertyName)
    {
        if (request.Params is JsonObject paramsObj &&
            paramsObj[propertyName] is JsonValue value &&
            value.TryGetValue(out string? result))
        {
            return result;
        }

        return null;
    }

    private static string[] GetConfiguredSupportedProtocolVersions(string? protocolVersion)
    {
        if (protocolVersion is null)
        {
            return McpProtocolVersions.SupportedProtocolVersions;
        }

        if (!McpProtocolVersions.IsSupportedProtocolVersion(protocolVersion))
        {
            throw new McpException(
                $"Unsupported server protocol version '{protocolVersion}'. Supported protocol versions: " +
                string.Join(", ", McpProtocolVersions.SupportedProtocolVersions) + ".");
        }

        return [protocolVersion];
    }

    private void ValidateNotificationBoundary(JsonRpcNotification notification)
    {
        if (notification.Method == NotificationMethods.InitializedNotification &&
            McpProtocolVersions.RequiresPerRequestMetadata(notification.Context?.ProtocolVersion ?? _negotiatedProtocolVersion))
        {
            throw new McpProtocolException(
                $"The notification '{NotificationMethods.InitializedNotification}' is only valid after the initialize handshake.",
                McpErrorCode.InvalidRequest);
        }
    }

    private void ValidateRequestMethodBoundary(JsonRpcRequest request)
    {
        bool usesPerRequestMetadata = IsJuly2026OrLaterProtocolRequest(request);

        if (!usesPerRequestMetadata &&
            request.Method is RequestMethods.SubscriptionsListen
                or RequestMethods.TasksGet
                or RequestMethods.TasksUpdate
                or RequestMethods.TasksCancel
                or RequestMethods.ServerDiscover)
        {
            throw new McpProtocolException(
                $"The method '{request.Method}' requires a newer protocol revision that supports per-request metadata; " +
                $"the negotiated protocol version is '{NegotiatedProtocolVersion ?? "(none)"}'.",
                McpErrorCode.MethodNotFound);
        }

        if (usesPerRequestMetadata && request.Method == RequestMethods.LoggingSetLevel)
        {
            throw new McpProtocolException(
                $"The method '{RequestMethods.LoggingSetLevel}' is not available on protocol version '{request.Context?.ProtocolVersion ?? NegotiatedProtocolVersion}'. Use per-request _meta/{MetaKeys.LogLevel} instead.",
                McpErrorCode.MethodNotFound);
        }
    }

    /// <inheritdoc/>
    public override string? SessionId => _sessionTransport.SessionId;

    /// <inheritdoc/>
    public override string? NegotiatedProtocolVersion => _negotiatedProtocolVersion;

    /// <summary>
    /// Records the negotiated MCP protocol version for the session. The version is established exactly
    /// once: the initial <see langword="null"/>-to-value transition is allowed (and racing requests that
    /// select the same version are idempotent no-ops), but any later attempt to switch to a different
    /// version throws. A single session MUST NOT change protocol versions, so a conflicting per-request
    /// <c>_meta</c> protocol version (or <c>Mcp-Protocol-Version</c> header) is a client error rather than
    /// something we silently overwrite.
    /// </summary>
    private void SetNegotiatedProtocolVersion(string protocolVersion)
    {
        string? previous = Interlocked.CompareExchange(ref _negotiatedProtocolVersion, protocolVersion, null);
        if (previous is null)
        {
            // We won the initial null-to-value transition; publish it to the session handler for telemetry.
            _sessionHandler.NegotiatedProtocolVersion = protocolVersion;
        }
        else if (!string.Equals(previous, protocolVersion, StringComparison.Ordinal))
        {
            throw new McpProtocolException(
                $"The negotiated protocol version cannot change within a session. " +
                $"The session negotiated '{previous}', but a request specified '{protocolVersion}'.",
                McpErrorCode.InvalidRequest);
        }
    }

    /// <inheritdoc/>
    public ServerCapabilities ServerCapabilities { get; }

    /// <inheritdoc />
    public override ClientCapabilities? ClientCapabilities => _clientCapabilities;

    /// <inheritdoc />
    public override Implementation? ClientInfo => _clientInfo;

    /// <inheritdoc />
    public override McpServerOptions ServerOptions { get; }

    /// <inheritdoc />
    public override IServiceProvider? Services { get; }

    /// <inheritdoc />
    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public override LoggingLevel? LoggingLevel => _loggingLevel?.Value;

    /// <inheritdoc />
    public override async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException($"{nameof(RunAsync)} must only be called once.");
        }

        try
        {
            await _sessionHandler.ProcessMessagesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }


    /// <inheritdoc/>
    public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        => _sessionHandler.SendRequestAsync(request, cancellationToken);

    /// <inheritdoc/>
    public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        => _sessionHandler.SendMessageAsync(message, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler)
        => _sessionHandler.RegisterNotificationHandler(method, handler);

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        using var _ = await _disposeLock.LockAsync().ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var kvp in _taskCancellationSources)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _taskCancellationSources.Clear();

        // Dispose the session handler - cancels message processing and waits for all
        // in-flight request handlers (including retries in AwaitMrtrHandlerAsync) to complete.
        // After this returns, no new requests can be processed and no new MRTR continuations
        // can be created, so _mrtrContinuations is effectively frozen.
        _disposables.ForEach(d => d());
        await _sessionHandler.DisposeAsync().ConfigureAwait(false);

        // Cancel all orphaned MRTR handlers still suspended in continuations (waiting for
        // retries that will never arrive now that the session handler is disposed).
        int cancelledCount = _mrtrContinuations.Count;
        foreach (var continuation in _mrtrContinuations.Values)
        {
            continuation.CancelHandler();
        }

        if (cancelledCount > 0)
        {
            MrtrContinuationsCancelled(cancelledCount);
        }

        // Wait for all MRTR handler tasks to complete using the same inFlightCount + TCS
        // pattern as McpSessionHandler.ProcessMessagesCoreAsync. The count started at 1
        // (for DisposeAsync itself); decrementing it here triggers the drain if handlers
        // are still in flight. ObserveHandlerCompletionAsync decrements for each handler.
        if (Interlocked.Decrement(ref _mrtrInFlightCount) != 0)
        {
            await _allMrtrHandlersCompleted.Task.ConfigureAwait(false);
        }
    }

    private void ConfigureInitialize(McpServerOptions options)
    {
        _requestHandlers.Set(RequestMethods.Initialize,
            async (request, _, _) =>
            {
                _clientCapabilities = request?.Capabilities ?? new();
                _clientInfo = request?.ClientInfo;

                // Use the ClientInfo to update the session EndpointName for logging.
                UpdateEndpointNameWithClientInfo();
                _sessionHandler.EndpointName = _endpointName;

                // Negotiate an initialize-handshake protocol version. initialize is not available in the 2026-07-28
                // and later protocol revisions, so those versions must use server/discover with
                // per-request _meta instead.
                string? protocolVersion = options.ProtocolVersion;
                if (protocolVersion is { } configuredProtocolVersion &&
                    McpProtocolVersions.IsJuly2026OrLaterProtocolVersion(configuredProtocolVersion))
                {
                    throw new UnsupportedProtocolVersionException(
                        configuredProtocolVersion,
                        _initializeHandshakeProtocolVersions,
                        $"Protocol version '{configuredProtocolVersion}' is not available through the initialize handshake.");
                }

                if (protocolVersion is null)
                {
                    if (request?.ProtocolVersion is string clientProtocolVersion)
                    {
                        if (McpProtocolVersions.IsJuly2026OrLaterProtocolVersion(clientProtocolVersion))
                        {
                            throw new UnsupportedProtocolVersionException(
                                clientProtocolVersion,
                                _initializeHandshakeProtocolVersions,
                                $"Protocol version '{clientProtocolVersion}' is not available through the initialize handshake.");
                        }

                        protocolVersion = McpProtocolVersions.SupportsInitializeHandshake(clientProtocolVersion) ?
                            clientProtocolVersion :
                            McpProtocolVersions.November2025ProtocolVersion;
                    }
                    else
                    {
                        protocolVersion = McpProtocolVersions.November2025ProtocolVersion;
                    }
                }

                string negotiatedProtocolVersion = protocolVersion ?? McpProtocolVersions.November2025ProtocolVersion;

                // The initialize handshake is authoritative: it may supersede a protocol version
                // a prior server/discover probe established on the same connection (the dual-path
                // fallback path a permissive client takes against an unknown server). Unlike the
                // per-request 2026-07-28 version - which SetNegotiatedProtocolVersion locks once negotiated -
                // initialize force-sets the version.
                _negotiatedProtocolVersion = negotiatedProtocolVersion;
                _sessionHandler.NegotiatedProtocolVersion = negotiatedProtocolVersion;

                return new InitializeResult
                {
                    ProtocolVersion = negotiatedProtocolVersion,
                    Instructions = options.ServerInstructions,
                    ServerInfo = options.ServerInfo ?? DefaultImplementation,
                    Capabilities = ServerCapabilities ?? new(),
                    ResultType = "complete",
                };
            },
            McpJsonUtilities.JsonContext.Default.InitializeRequestParams,
            McpJsonUtilities.JsonContext.Default.InitializeResult);
    }

    /// <summary>
    /// Registers the <c>server/discover</c> request handler introduced by the 2026-07-28 protocol revision (SEP-2575).
    /// </summary>
    /// <remarks>
    /// The handler is registered unconditionally so requests can be routed to the protocol boundary filters. Successful
    /// <c>server/discover</c> responses advertise only protocol versions available through per-request metadata; versions
    /// that require the <c>initialize</c> handshake are negotiated through <c>initialize</c> instead.
    /// </remarks>
    private void ConfigureDiscover(McpServerOptions options)
    {
        _requestHandlers.Set(RequestMethods.ServerDiscover,
            (request, _, _) =>
            {
                return new ValueTask<DiscoverResult>(new DiscoverResult
                {
                    SupportedVersions = [.. _perRequestMetadataProtocolVersions],
                    Capabilities = ServerCapabilities ?? new(),
                    ServerInfo = options.ServerInfo ?? DefaultImplementation,
                    Instructions = options.ServerInstructions,
                    // Spec PR #2855 makes ttlMs and cacheScope required on DiscoverResult. Default to
                    // the safest values (immediately stale, not shareable) so existing servers keep
                    // their "do not cache" behavior while satisfying the wire requirement.
                    TimeToLive = TimeSpan.Zero,
                    CacheScope = CacheScope.Private,
                    ResultType = "complete",
                });
            },
            McpJsonUtilities.JsonContext.Default.DiscoverRequestParams,
            McpJsonUtilities.JsonContext.Default.DiscoverResult);
    }

    /// <summary>
    /// Registers the <c>subscriptions/listen</c> request handler introduced by the 2026-07-28 protocol revision (SEP-2575).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler opens a long-lived response stream (over the per-request <see cref="StreamableHttpPostTransport"/>
    /// for HTTP, or the shared STDIO channel) that first sends
    /// <see cref="NotificationMethods.SubscriptionsAcknowledgedNotification"/> reporting which subscriptions the
    /// server agreed to honor, and then streams matching notifications until the request is cancelled.
    /// </para>
    /// <para>
    /// Subscription-bound notifications carry the listen request's id in their
    /// <c>_meta/io.modelcontextprotocol/subscriptionId</c> field per SEP-2575 so clients can demultiplex.
    /// </para>
    /// </remarks>
    private void ConfigureSubscriptions(McpServerOptions options)
    {
        _requestHandlers.Set(RequestMethods.SubscriptionsListen,
            async (request, jsonRpcRequest, cancellationToken) =>
            {
                if (!IsJuly2026OrLaterProtocolRequest(jsonRpcRequest))
                {
                    throw new McpProtocolException(
                        $"The method '{RequestMethods.SubscriptionsListen}' requires a newer protocol revision that supports per-request subscriptions; " +
                        $"the negotiated protocol version is '{NegotiatedProtocolVersion ?? "(none)"}'.",
                        McpErrorCode.MethodNotFound);
                }

                var requested = request?.Notifications ?? new SubscriptionsListenNotifications();

                // A stateless session (Streamable HTTP with no session) cannot deliver out-of-band
                // notifications: each request is isolated and nothing outlives it to push later list/resource
                // changes back to the client (tracked by #1662). Rather than hold the POST open forever only
                // to deliver nothing - pinning the connection and its request scope - acknowledge the listen
                // request granting no notifications and complete immediately. This runs after protocol
                // negotiation, so it is not an initialize-handshake-server signal and never triggers a client fallback to the
                // initialize handshake.
                if (!HasStatefulTransport())
                {
                    var statelessSubscription = new ActiveSubscription(
                        jsonRpcRequest.Id,
                        new SubscriptionsListenNotifications(),
                        jsonRpcRequest.Context?.RelatedTransport);

                    await SendSubscriptionAckAsync(statelessSubscription, cancellationToken).ConfigureAwait(false);

                    return EmptyResult.Instance;
                }

                // Filter the requested notifications against what the server actually supports.
                var granted = new SubscriptionsListenNotifications
                {
                    ToolsListChanged = requested.ToolsListChanged == true && ServerCapabilities?.Tools?.ListChanged == true ? true : null,
                    PromptsListChanged = requested.PromptsListChanged == true && ServerCapabilities?.Prompts?.ListChanged == true ? true : null,
                    ResourcesListChanged = requested.ResourcesListChanged == true && ServerCapabilities?.Resources?.ListChanged == true ? true : null,
                    ResourceSubscriptions = requested.ResourceSubscriptions is { Count: > 0 } subs && ServerCapabilities?.Resources?.Subscribe == true
                        ? new List<string>(subs)
                        : null,
                };

                // Track this subscription so list-changed notifications can be fanned out to it, tagged with
                // the right subscriptionId, and routed back over the stream this request opened.
                var subscription = new ActiveSubscription(
                    jsonRpcRequest.Id,
                    granted,
                    jsonRpcRequest.Context?.RelatedTransport);
                _activeSubscriptions[jsonRpcRequest.Id] = subscription;

                try
                {
                    // Send the acknowledgement notification first, as required by SEP-2575. Like every other
                    // notification delivered on the subscription it is routed back over this request's own
                    // stream and tagged with the subscription id so shared-channel clients can demultiplex it.
                    await SendSubscriptionAckAsync(subscription, cancellationToken).ConfigureAwait(false);

                    // Keep the subscription open until the request is cancelled (client disconnect on HTTP,
                    // or notifications/cancelled on STDIO).
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), tcs);
                    await tcs.Task.ConfigureAwait(false);
                }
                finally
                {
                    _activeSubscriptions.TryRemove(jsonRpcRequest.Id, out _);
                }

                return EmptyResult.Instance;
            },
            McpJsonUtilities.JsonContext.Default.SubscriptionsListenRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);
    }

    /// <summary>Tracks an active <c>subscriptions/listen</c> subscription for notification fan-out.</summary>
    /// <param name="Id">The id of the <c>subscriptions/listen</c> request, reused as the SEP-2575 subscription id.</param>
    /// <param name="Granted">The notification types the server agreed to deliver on this subscription.</param>
    /// <param name="RelatedTransport">
    /// The transport the <c>subscriptions/listen</c> request arrived on. For Streamable HTTP this is the
    /// per-request response stream the subscription must be delivered on; for stdio it is <see langword="null"/>,
    /// so notifications fall back to the shared session channel.
    /// </param>
    private sealed record ActiveSubscription(RequestId Id, SubscriptionsListenNotifications Granted, ITransport? RelatedTransport);

    private readonly ConcurrentDictionary<RequestId, ActiveSubscription> _activeSubscriptions = new();

    /// <summary>
    /// Delivers a <c>*/list_changed</c> notification triggered by a server-side collection change.
    /// </summary>
    /// <remarks>
    /// Pre-SEP-2575 clients do not open <c>subscriptions/listen</c> streams, so they keep receiving a single
    /// session-wide broadcast. Clients on the 2026-07-28 or later revision instead receive only the change notifications they explicitly
    /// requested, each routed back over the originating subscription stream and tagged with its id; the server
    /// <b>MUST NOT</b> send such a client notification types it never subscribed to.
    /// </remarks>
    private async Task SendListChangedNotificationAsync(string notificationMethod)
    {
        // Initialize-handshake clients never open a subscriptions/listen stream, so they keep the session-wide broadcast.
        // subscriptions/listen is a SEP-2575 feature, so clients on the 2026-07-28 or later revision instead get
        // a fan-out limited to the notification types they explicitly subscribed to.
        if (!IsJuly2026OrLaterProtocol())
        {
            await this.SendNotificationAsync(notificationMethod).ConfigureAwait(false);
            return;
        }

        foreach (var subscription in _activeSubscriptions.Values)
        {
            if (!GrantsListChanged(subscription.Granted, notificationMethod))
            {
                continue;
            }

            try
            {
                await SendSubscriptionNotificationAsync(subscription, notificationMethod, paramsNode: null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A single closed or faulted subscription stream must not prevent fan-out to the others.
                SubscriptionNotificationFailed(notificationMethod, subscription.Id.ToString(), ex);
            }
        }
    }

    /// <summary>
    /// Sends <paramref name="method"/> over <paramref name="subscription"/>'s stream, tagging it with the
    /// SEP-2575 <c>_meta</c> subscription id so clients sharing a channel (notably stdio) can demultiplex it.
    /// </summary>
    private Task SendSubscriptionNotificationAsync(ActiveSubscription subscription, string method, JsonNode? paramsNode, CancellationToken cancellationToken)
    {
        var paramsObject = paramsNode as JsonObject ?? new JsonObject();
        if (paramsObject["_meta"] is not JsonObject meta)
        {
            meta = new JsonObject();
            paramsObject["_meta"] = meta;
        }

        meta[MetaKeys.SubscriptionId] = subscription.Id.Id switch
        {
            string stringId => JsonValue.Create(stringId),
            long longId => JsonValue.Create(longId),
            _ => null,
        };

        var notification = new JsonRpcNotification
        {
            Method = method,
            Params = paramsObject,
            Context = new JsonRpcMessageContext { RelatedTransport = subscription.RelatedTransport },
        };

        return SendMessageAsync(notification, cancellationToken);
    }

    /// <summary>
    /// Sends the SEP-2575 <c>subscriptions/acknowledged</c> notification for a subscription, carrying the
    /// notification types the server agreed to deliver. Routed back over the subscription's own stream and
    /// tagged with its id like every other subscription notification.
    /// </summary>
    private Task SendSubscriptionAckAsync(ActiveSubscription subscription, CancellationToken cancellationToken)
    {
        var ackParams = JsonSerializer.SerializeToNode(
            new SubscriptionsAcknowledgedNotificationParams { Notifications = subscription.Granted },
            McpJsonUtilities.JsonContext.Default.SubscriptionsAcknowledgedNotificationParams);

        return SendSubscriptionNotificationAsync(
            subscription,
            NotificationMethods.SubscriptionsAcknowledgedNotification,
            ackParams,
            cancellationToken);
    }

    /// <summary>Maps a <c>*/list_changed</c> method to the subscription filter flag that enables it.</summary>
    private static bool GrantsListChanged(SubscriptionsListenNotifications granted, string method) => method switch
    {
        NotificationMethods.ToolListChangedNotification => granted.ToolsListChanged == true,
        NotificationMethods.PromptListChangedNotification => granted.PromptsListChanged == true,
        NotificationMethods.ResourceListChangedNotification => granted.ResourcesListChanged == true,
        _ => false,
    };

    private void ConfigureCompletion(McpServerOptions options)
    {
        var completeHandler = options.Handlers.CompleteHandler;
        var completionsCapability = options.Capabilities?.Completions;

        // Build completion value lookups from prompt/resource collections' [AllowedValues]-attributed parameters.
        Dictionary<string, Dictionary<string, string[]>>? promptCompletions = BuildAllowedValueCompletions(options.PromptCollection);
        Dictionary<string, Dictionary<string, string[]>>? resourceCompletions = BuildAllowedValueCompletions(options.ResourceCollection);
        bool hasCollectionCompletions = promptCompletions is not null || resourceCompletions is not null;

        if (completeHandler is null && completionsCapability is null && !hasCollectionCompletions)
        {
            return;
        }

        completeHandler ??= (static async (_, __) => new CompleteResult());

        // Augment the completion handler with allowed values from prompt/resource collections.
        if (hasCollectionCompletions)
        {
            var originalCompleteHandler = completeHandler;
            completeHandler = async (request, cancellationToken) =>
            {
                CompleteResult result = await originalCompleteHandler(request, cancellationToken).ConfigureAwait(false);

                string[]? allowedValues = null;
                switch (request.Params?.Ref)
                {
                    case PromptReference pr when promptCompletions is not null:
                        if (promptCompletions.TryGetValue(pr.Name, out var promptParams))
                        {
                            promptParams.TryGetValue(request.Params.Argument.Name, out allowedValues);
                        }
                        break;

                    case ResourceTemplateReference rtr when resourceCompletions is not null:
                        if (rtr.Uri is not null && resourceCompletions.TryGetValue(rtr.Uri, out var resourceParams))
                        {
                            resourceParams.TryGetValue(request.Params.Argument.Name, out allowedValues);
                        }
                        break;
                }

                if (allowedValues is not null)
                {
                    string partialValue = request.Params!.Argument.Value;
                    foreach (var v in allowedValues)
                    {
                        if (v.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Completion.Values.Add(v);
                        }
                    }

                    result.Completion.Total = result.Completion.Values.Count;
                }

                return result;
            };
        }

        completeHandler = BuildFilterPipeline(completeHandler, options.Filters.Request.CompleteFilters);

        ServerCapabilities.Completions = new();

        SetHandler(
            RequestMethods.CompletionComplete,
            completeHandler,
            McpJsonUtilities.JsonContext.Default.CompleteRequestParams,
            McpJsonUtilities.JsonContext.Default.CompleteResult);
    }

    /// <summary>
    /// Builds a lookup of primitive name/URI → (parameter name → allowed values) from the enum values
    /// in the JSON schemas of AIFunction-based prompts or resources.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string[]>>? BuildAllowedValueCompletions<T>(
        McpServerPrimitiveCollection<T>? primitives) where T : class, IMcpServerPrimitive
    {
        if (primitives is null)
        {
            return null;
        }

        Dictionary<string, Dictionary<string, string[]>>? result = null;
        foreach (var primitive in primitives)
        {
            JsonElement schema;
            string id;
            if (primitive is AIFunctionMcpServerPrompt aiPrompt)
            {
                schema = aiPrompt.AIFunction.JsonSchema;
                id = aiPrompt.ProtocolPrompt.Name;
            }
            else if (primitive is AIFunctionMcpServerResource aiResource && aiResource.IsTemplated)
            {
                schema = aiResource.AIFunction.JsonSchema;
                id = aiResource.ProtocolResourceTemplate.UriTemplate;
            }
            else
            {
                continue;
            }

            if (schema.TryGetProperty("properties", out JsonElement properties) &&
                properties.ValueKind is JsonValueKind.Object)
            {
                Dictionary<string, string[]>? paramValues = null;
                foreach (var param in properties.EnumerateObject())
                {
                    if (param.Value.TryGetProperty("enum", out JsonElement enumValues) &&
                        enumValues.ValueKind is JsonValueKind.Array)
                    {
                        List<string>? values = null;
                        foreach (var item in enumValues.EnumerateArray())
                        {
                            if (item.ValueKind is JsonValueKind.String && item.GetString() is { } str)
                            {
                                values ??= [];
                                values.Add(str);
                            }
                        }

                        if (values is not null)
                        {
                            paramValues ??= new(StringComparer.Ordinal);
                            paramValues[param.Name] = [.. values];
                        }
                    }
                }

                if (paramValues is not null)
                {
                    result ??= new(StringComparer.Ordinal);
                    result[id] = paramValues;
                }
            }
        }

        return result;
    }

    private void ConfigureTasks(McpServerOptions options)
    {
        var getTaskHandler = options.Handlers.GetTaskHandler;
        var updateTaskHandler = options.Handlers.UpdateTaskHandler;
        var cancelTaskHandler = options.Handlers.CancelTaskHandler;
        var taskStore = options.TaskStore;

        // If a task store is provided, wire up handlers from it for any that aren't explicitly set.
        if (taskStore is not null)
        {
            getTaskHandler ??= async (request, cancellationToken) =>
            {
                var info = await taskStore.GetTaskAsync(request.Params!.TaskId, cancellationToken).ConfigureAwait(false);
                return info is null
                    ? throw new McpProtocolException($"Unknown task: '{request.Params.TaskId}'", McpErrorCode.InvalidParams)
                    : ToGetTaskResult(info);
            };

            updateTaskHandler ??= async (request, cancellationToken) =>
            {
                var inputResponses = request.Params!.InputResponses ?? new Dictionary<string, InputResponse>();
                await taskStore.ResolveInputRequestsAsync(request.Params.TaskId, inputResponses, cancellationToken).ConfigureAwait(false);

                return new UpdateTaskResult();
            };

            cancelTaskHandler ??= async (request, cancellationToken) =>
            {
                // Idempotent ack per SEP-2663: always return CancelTaskResult regardless of whether
                // the task was known/cancellable. The store's SetCancelledAsync no-ops for unknown
                // or already-terminal tasks; we still surface a success response to the client.
                await taskStore.SetCancelledAsync(request.Params!.TaskId, cancellationToken).ConfigureAwait(false);

                // Signal the task's CancellationTokenSource if one exists. Whichever side
                // (this handler or the background runner's finally block) wins TryRemove owns disposal,
                // which prevents the runner from observing ObjectDisposedException through cts.Token.
                if (_taskCancellationSources.TryRemove(request.Params.TaskId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                return new CancelTaskResult();
            };
        }

        if (getTaskHandler is null && updateTaskHandler is null && cancelTaskHandler is null)
        {
            return;
        }

        getTaskHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown task: '{request.Params?.TaskId}'", McpErrorCode.InvalidParams));
        updateTaskHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown task: '{request.Params?.TaskId}'", McpErrorCode.InvalidParams));
        cancelTaskHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown task: '{request.Params?.TaskId}'", McpErrorCode.InvalidParams));

        // The tasks/* methods do not exist before the 2026-07-28 revision (SEP-2663). Reject them with
        // MethodNotFound when the request was negotiated under an initialize-handshake protocol version. The handlers
        // stay registered so a dual-path server still serves them for 2026-07-28 requests.
        getTaskHandler = GateTaskMethodToJuly2026OrLaterProtocol(getTaskHandler, RequestMethods.TasksGet);
        updateTaskHandler = GateTaskMethodToJuly2026OrLaterProtocol(updateTaskHandler, RequestMethods.TasksUpdate);
        cancelTaskHandler = GateTaskMethodToJuly2026OrLaterProtocol(cancelTaskHandler, RequestMethods.TasksCancel);

        // Advertise tasks extension in server capabilities.
        ServerCapabilities.Extensions ??= new Dictionary<string, object>();
        ServerCapabilities.Extensions[McpExtensions.Tasks] = new JsonObject();

        SetHandler(
            RequestMethods.TasksGet,
            getTaskHandler,
            McpJsonUtilities.JsonContext.Default.GetTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.GetTaskResult);

        SetHandler(
            RequestMethods.TasksUpdate,
            updateTaskHandler,
            McpJsonUtilities.JsonContext.Default.UpdateTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.UpdateTaskResult);

        SetHandler(
            RequestMethods.TasksCancel,
            cancelTaskHandler,
            McpJsonUtilities.JsonContext.Default.CancelTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.CancelTaskResult);
    }

    /// <summary>
    /// Wraps a tasks/* request handler so it throws <see cref="McpErrorCode.MethodNotFound"/> unless the
    /// request was negotiated under the 2026-07-28 or later revision. The tasks extension (SEP-2663) only
    /// interoperates on the 2026-07-28 revision, and these methods don't exist on older peers.
    /// </summary>
    private McpRequestHandler<TParams, TResult> GateTaskMethodToJuly2026OrLaterProtocol<TParams, TResult>(
        McpRequestHandler<TParams, TResult> inner, string method)
        => (request, cancellationToken) =>
        {
            if (!IsJuly2026OrLaterProtocolRequest(request.JsonRpcRequest))
            {
                throw new McpProtocolException(
                    $"The method '{method}' requires a newer protocol revision that supports tasks; " +
                    $"the negotiated protocol version is '{NegotiatedProtocolVersion ?? "(none)"}'.",
                    McpErrorCode.MethodNotFound);
            }

            return inner(request, cancellationToken);
        };

    private void ConfigureExperimentalAndExtensions(McpServerOptions options)
    {
        ServerCapabilities.Experimental = options.Capabilities?.Experimental;
        ServerCapabilities.Extensions = options.Capabilities?.Extensions;
    }

    private void ConfigureResources(McpServerOptions options)
    {
        var listResourcesHandler = options.Handlers.ListResourcesHandler;
        var listResourceTemplatesHandler = options.Handlers.ListResourceTemplatesHandler;
        var readResourceHandler = options.Handlers.ReadResourceHandler;
        var subscribeHandler = options.Handlers.SubscribeToResourcesHandler;
        var unsubscribeHandler = options.Handlers.UnsubscribeFromResourcesHandler;
        var resources = options.ResourceCollection;
        var resourcesCapability = options.Capabilities?.Resources;

        if (listResourcesHandler is null && listResourceTemplatesHandler is null && readResourceHandler is null &&
            subscribeHandler is null && unsubscribeHandler is null && resources is null &&
            resourcesCapability is null)
        {
            return;
        }

        ServerCapabilities.Resources = new();

        listResourcesHandler ??= (static async (_, __) => new ListResourcesResult());
        listResourceTemplatesHandler ??= (static async (_, __) => new ListResourceTemplatesResult());
        readResourceHandler ??= (static async (request, _) =>
        {
            var errorCode = McpProtocolVersions.UseInvalidParamsForMissingResource(request.Server.NegotiatedProtocolVersion)
                ? McpErrorCode.InvalidParams
                : McpErrorCode.ResourceNotFound;
            throw new McpProtocolException($"Unknown resource URI: '{request.Params?.Uri}'", errorCode);
        });
        subscribeHandler ??= (static async (_, __) => new EmptyResult());
        unsubscribeHandler ??= (static async (_, __) => new EmptyResult());
        var listChanged = resourcesCapability?.ListChanged;
        var subscribe = resourcesCapability?.Subscribe;

        // Handle resources provided via DI.
        if (resources is not null)
        {
            var originalListResourcesHandler = listResourcesHandler;
            listResourcesHandler = async (request, cancellationToken) =>
            {
                ListResourcesResult result = originalListResourcesHandler is not null ?
                    await originalListResourcesHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    foreach (var r in resources)
                    {
                        if (r.ProtocolResource is { } resource)
                        {
                            result.Resources.Add(resource);
                        }
                    }
                }

                return result;
            };

            var originalListResourceTemplatesHandler = listResourceTemplatesHandler;
            listResourceTemplatesHandler = async (request, cancellationToken) =>
            {
                ListResourceTemplatesResult result = originalListResourceTemplatesHandler is not null ?
                    await originalListResourceTemplatesHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    foreach (var rt in resources)
                    {
                        if (rt.IsTemplated)
                        {
                            result.ResourceTemplates.Add(rt.ProtocolResourceTemplate);
                        }
                    }
                }

                return result;
            };

            // Synthesize read resource handler, which covers both resources and resource templates.
            var originalReadResourceHandler = readResourceHandler;
            readResourceHandler = async (request, cancellationToken) =>
            {
                if (request.MatchedPrimitive is McpServerResource matchedResource)
                {
                    return await matchedResource.ReadAsync(request, cancellationToken).ConfigureAwait(false);
                }

                return await originalReadResourceHandler(request, cancellationToken).ConfigureAwait(false);
            };

            listChanged = true;

            // TODO: Implement subscribe/unsubscribe logic for resource and resource template collections.
            // subscribe = true;
        }

        listResourcesHandler = BuildFilterPipeline(listResourcesHandler, options.Filters.Request.ListResourcesFilters);
        listResourceTemplatesHandler = BuildFilterPipeline(listResourceTemplatesHandler, options.Filters.Request.ListResourceTemplatesFilters);
        readResourceHandler = BuildFilterPipeline(readResourceHandler, options.Filters.Request.ReadResourceFilters, handler =>
            async (request, cancellationToken) =>
            {
                // Initial handler that sets MatchedPrimitive
                if (request.Params?.Uri is { } uri && resources is not null)
                {
                    // First try an O(1) lookup by exact match.
                    if (resources.TryGetPrimitive(uri, out var resource) && !resource.IsTemplated)
                    {
                        request.MatchedPrimitive = resource;
                    }
                    else
                    {
                        // Fall back to an O(N) lookup, trying to match against each URI template.
                        foreach (var resourceTemplate in resources)
                        {
                            if (resourceTemplate.IsMatch(uri))
                            {
                                request.MatchedPrimitive = resourceTemplate;
                                break;
                            }
                        }
                    }
                }

                try
                {
                    var result = await handler(request, cancellationToken).ConfigureAwait(false);
                    ReadResourceCompleted(request.Params?.Uri ?? string.Empty);
                    return result;
                }
                catch (Exception e)
                {
                    ReadResourceError(request.Params?.Uri ?? string.Empty, e);
                    throw;
                }
            });
        subscribeHandler = BuildFilterPipeline(subscribeHandler, options.Filters.Request.SubscribeToResourcesFilters);
        unsubscribeHandler = BuildFilterPipeline(unsubscribeHandler, options.Filters.Request.UnsubscribeFromResourcesFilters);

        ServerCapabilities.Resources.ListChanged = listChanged;
        ServerCapabilities.Resources.Subscribe = subscribe;

        SetHandler(
            RequestMethods.ResourcesList,
            listResourcesHandler,
            McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourcesResult);

        SetHandler(
            RequestMethods.ResourcesTemplatesList,
            listResourceTemplatesHandler,
            McpJsonUtilities.JsonContext.Default.ListResourceTemplatesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourceTemplatesResult);

        SetHandler(
            RequestMethods.ResourcesRead,
            readResourceHandler,
            McpJsonUtilities.JsonContext.Default.ReadResourceRequestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceResult);

        SetHandler(
            RequestMethods.ResourcesSubscribe,
            subscribeHandler,
            McpJsonUtilities.JsonContext.Default.SubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);

        SetHandler(
            RequestMethods.ResourcesUnsubscribe,
            unsubscribeHandler,
            McpJsonUtilities.JsonContext.Default.UnsubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);
    }

    private void ConfigurePrompts(McpServerOptions options)
    {
        var listPromptsHandler = options.Handlers.ListPromptsHandler;
        var getPromptHandler = options.Handlers.GetPromptHandler;
        var prompts = options.PromptCollection;
        var promptsCapability = options.Capabilities?.Prompts;

        if (listPromptsHandler is null && getPromptHandler is null && prompts is null &&
            promptsCapability is null)
        {
            return;
        }

        ServerCapabilities.Prompts = new();

        listPromptsHandler ??= (static async (_, __) => new ListPromptsResult());
        getPromptHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown prompt: '{request.Params?.Name}'", McpErrorCode.InvalidParams));
        var listChanged = promptsCapability?.ListChanged;

        // Handle tools provided via DI by augmenting the handlers to incorporate them.
        if (prompts is not null)
        {
            var originalListPromptsHandler = listPromptsHandler;
            listPromptsHandler = async (request, cancellationToken) =>
            {
                ListPromptsResult result = originalListPromptsHandler is not null ?
                    await originalListPromptsHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    foreach (var p in prompts)
                    {
                        result.Prompts.Add(p.ProtocolPrompt);
                    }
                }

                return result;
            };

            var originalGetPromptHandler = getPromptHandler;
            getPromptHandler = (request, cancellationToken) =>
            {
                if (request.MatchedPrimitive is McpServerPrompt prompt)
                {
                    return prompt.GetAsync(request, cancellationToken);
                }

                return originalGetPromptHandler(request, cancellationToken);
            };

            listChanged = true;
        }

        listPromptsHandler = BuildFilterPipeline(listPromptsHandler, options.Filters.Request.ListPromptsFilters);
        getPromptHandler = BuildFilterPipeline(getPromptHandler, options.Filters.Request.GetPromptFilters, handler =>
            async (request, cancellationToken) =>
            {
                // Initial handler that sets MatchedPrimitive
                if (request.Params?.Name is { } promptName && prompts is not null &&
                    prompts.TryGetPrimitive(promptName, out var prompt))
                {
                    request.MatchedPrimitive = prompt;
                }

                try
                {
                    var result = await handler(request, cancellationToken).ConfigureAwait(false);
                    GetPromptCompleted(request.Params?.Name ?? string.Empty);
                    return result;
                }
                catch (Exception e)
                {
                    GetPromptError(request.Params?.Name ?? string.Empty, e);
                    throw;
                }
            });

        ServerCapabilities.Prompts.ListChanged = listChanged;

        SetHandler(
            RequestMethods.PromptsList,
            listPromptsHandler,
            McpJsonUtilities.JsonContext.Default.ListPromptsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListPromptsResult);

        SetHandler(
            RequestMethods.PromptsGet,
            getPromptHandler,
            McpJsonUtilities.JsonContext.Default.GetPromptRequestParams,
            McpJsonUtilities.JsonContext.Default.GetPromptResult);
    }

    private void ConfigureTools(McpServerOptions options)
    {
        var listToolsHandler = options.Handlers.ListToolsHandler;
        var callToolHandler = options.Handlers.CallToolHandler;
        var callToolWithTaskHandler = options.Handlers.CallToolWithTaskHandler;
        var tools = options.ToolCollection;
        var toolsCapability = options.Capabilities?.Tools;

        if (listToolsHandler is null && callToolHandler is null && callToolWithTaskHandler is null && tools is null &&
            toolsCapability is null)
        {
            return;
        }

        ServerCapabilities.Tools = new();

        listToolsHandler ??= (static async (_, __) => new ListToolsResult());
        var listChanged = toolsCapability?.ListChanged;

        var callToolFilters = options.Filters.Request.CallToolFilters;
        var callToolWithTaskFilters = options.Filters.Request.CallToolWithTaskFilters;

        // Validate: cannot mix non-task filters/handler with task filters/handler.
        bool hasNonTaskPath = callToolHandler is not null || callToolFilters.Count > 0;
        bool hasTaskPath = callToolWithTaskHandler is not null || callToolWithTaskFilters.Count > 0;

        if (hasNonTaskPath && hasTaskPath)
        {
            throw new InvalidOperationException(
                $"Cannot mix non-task ({nameof(McpServerHandlers.CallToolHandler)}/{nameof(McpRequestFilters.CallToolFilters)}) " +
                $"with task-based ({nameof(McpServerHandlers.CallToolWithTaskHandler)}/{nameof(McpRequestFilters.CallToolWithTaskFilters)}). Use one style or the other.");
        }

        // Handle tools provided via DI by augmenting the list handler.
        if (tools is not null)
        {
            var originalListToolsHandler = listToolsHandler;
            listToolsHandler = async (request, cancellationToken) =>
            {
                ListToolsResult result = originalListToolsHandler is not null ?
                    await originalListToolsHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    // SEP-2106 wire shaping: clients on protocol versions older than
                    // 2026-07-28 require outputSchema.type == "object", so the natural
                    // schema is reshaped before emission (type:["object","null"] normalized
                    // to "object", any other non-object schema wrapped in
                    // {"type":"object","properties":{"result":<schema>}}). Clients on
                    // 2026-07-28+ receive the natural JSON Schema 2020-12 document stored
                    // on Tool.OutputSchema. Only AIFunctionMcpServerTool tools go through
                    // reshaping; custom McpServerTool subclasses build their Tool directly
                    // and pass through unchanged at every protocol version.
                    bool useNaturalSchemas = McpSessionHandler.SupportsNaturalOutputSchemas(request.Server.NegotiatedProtocolVersion);
                    foreach (var t in tools)
                    {
                        Tool wireTool = useNaturalSchemas || t is not AIFunctionMcpServerTool aiFunctionTool
                            ? t.ProtocolTool
                            : aiFunctionTool.BuildLegacyWireProtocolTool();
                        result.Tools.Add(wireTool);
                    }
                }

                return result;
            };

            listChanged = true;
        }

        listToolsHandler = BuildFilterPipeline(listToolsHandler, options.Filters.Request.ListToolsFilters);

        // Build the unified task-augmented handler from one of the two paths.
        if (hasTaskPath)
        {
            // Case 2: task filter + task handler
            callToolWithTaskHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown tool: '{request.Params?.Name}'", McpErrorCode.InvalidParams));

            // Augment with DI tools.
            if (tools is not null)
            {
                var originalHandler = callToolWithTaskHandler;
                callToolWithTaskHandler = (request, cancellationToken) =>
                {
                    if (request.MatchedPrimitive is McpServerTool tool)
                    {
                        return InvokeToolAsTask(tool, request, cancellationToken);
                    }

                    return originalHandler(request, cancellationToken);
                };
            }

            callToolWithTaskHandler = BuildFilterPipeline(callToolWithTaskHandler, callToolWithTaskFilters, BuildInitialTaskToolFilter(tools));
        }
        else
        {
            // Case 1: non-task filter + non-task handler → apply filters, then convert to task-based
            callToolHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown tool: '{request.Params?.Name}'", McpErrorCode.InvalidParams));

            // Augment with DI tools.
            if (tools is not null)
            {
                var originalHandler = callToolHandler;
                callToolHandler = (request, cancellationToken) =>
                {
                    if (request.MatchedPrimitive is McpServerTool tool)
                    {
                        return tool.InvokeAsync(request, cancellationToken);
                    }

                    return originalHandler(request, cancellationToken);
                };
            }

            callToolHandler = BuildFilterPipeline(callToolHandler, callToolFilters, BuildInitialCallToolFilter(tools));

            // Convert to task-based.
            var finalCallToolHandler = callToolHandler;
            callToolWithTaskHandler = async (request, cancellationToken) =>
                await finalCallToolHandler(request, cancellationToken).ConfigureAwait(false);
        }

        // If a task store is configured, wrap so that when the client signals task support
        // the tool execution is offloaded to the background via the store.
        if (options.TaskStore is { } taskStore)
        {
            var innerTaskHandler = callToolWithTaskHandler;
            callToolWithTaskHandler = async (request, cancellationToken) =>
            {
                // The SEP-2663 Tasks extension requires the 2026-07-28 or later revision: the task wire shapes we ship do not
                // interoperate with legacy (<= 2025-11-25) peers. Only materialize a task when the
                // request was negotiated under the 2026-07-28 or later revision AND the client opted in; otherwise
                // run the inner handler and return the direct result (best-effort downgrade, which also
                // defends against a non-conformant legacy client that forges the opt-in envelope).
                if (IsJuly2026OrLaterProtocolRequest(request.JsonRpcRequest) && HasTaskExtensionOptIn(request.Params?.Meta))
                {
                    var taskInfo = await taskStore.CreateTaskAsync(cancellationToken).ConfigureAwait(false);
                    var taskId = taskInfo.TaskId;

                    var cts = new CancellationTokenSource();
                    _taskCancellationSources[taskId] = cts;

                    // Capture the token synchronously before Task.Run dispatches the work.
                    // The cancel handler may race with the background runner: whichever side wins
                    // the TryRemove call owns disposal. If we accessed cts.Token from inside the
                    // lambda after the handler had already disposed cts, we'd hit ObjectDisposedException.
                    var taskCancellationToken = cts.Token;

                    _ = Task.Run(async () =>
                    {
                        using (CreateMcpTaskScope(taskId, taskStore))
                        {
                            try
                            {
                                var augmented = await innerTaskHandler(request, taskCancellationToken).ConfigureAwait(false);
                                if (augmented.IsTask)
                                {
                                    // The handler created its own task externally, but the client already holds
                                    // the store's taskId from the synchronous return below — we can't redirect.
                                    // Fail the store's task so the client sees a clear error instead of polling forever.
                                    var error = new JsonRpcErrorDetail
                                    {
                                        Code = (int)McpErrorCode.InternalError,
                                        Message = $"{nameof(McpServerOptions.TaskStore)} is configured and the {nameof(McpServerHandlers.CallToolWithTaskHandler)} returned IsTask = true. Use only one mechanism to create the task.",
                                    };
                                    var errorJson = JsonSerializer.SerializeToElement(error, McpJsonUtilities.JsonContext.Default.JsonRpcErrorDetail);
                                    await taskStore.SetFailedAsync(taskId, errorJson).ConfigureAwait(false);
                                    return;
                                }

                                var resultJson = JsonSerializer.SerializeToElement(augmented.Result!, McpJsonUtilities.JsonContext.Default.CallToolResult);
                                await taskStore.SetCompletedAsync(taskId, resultJson).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (taskCancellationToken.IsCancellationRequested)
                            {
                                await taskStore.SetCancelledAsync(taskId, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (InputRequiredException)
                            {
                                // MRTR (input requests) cannot be composed with the task-store wrapper for
                                // [McpServerTool] methods today: the task ID was already returned synchronously,
                                // so we have no way to surface InputRequiredResult to the client retroactively.
                                // Fail the task with a clear, actionable error instead of leaking the raw
                                // InputRequiredException through the generic catch below.
                                var error = new JsonRpcErrorDetail
                                {
                                    Code = (int)McpErrorCode.InvalidRequest,
                                    Message = "MRTR (input requests) and tasks cannot be composed via [McpServerTool] yet; " +
                                              $"use {nameof(McpServerHandlers.CallToolWithTaskHandler)} to manage the input-request loop manually within the task body.",
                                };
                                var errorJson = JsonSerializer.SerializeToElement(error, McpJsonUtilities.JsonContext.Default.JsonRpcErrorDetail);
                                await taskStore.SetFailedAsync(taskId, errorJson).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                // SEP-2663 §186: failed.error MUST be a JSON-RPC error object {code, message, data?}.
                                // McpProtocolException carries a JSON-RPC ErrorCode and is documented as safe to
                                // propagate (Message + ErrorCode). For any other exception type, redact the message
                                // and use InternalError (mirrors the redaction in BuildInitialCallToolFilter).
                                var error = ex is McpProtocolException mcpEx
                                    ? new JsonRpcErrorDetail { Code = (int)mcpEx.ErrorCode, Message = mcpEx.Message }
                                    : new JsonRpcErrorDetail { Code = (int)McpErrorCode.InternalError, Message = "An error occurred while executing the task." };
                                var errorJson = JsonSerializer.SerializeToElement(error, McpJsonUtilities.JsonContext.Default.JsonRpcErrorDetail);
                                await taskStore.SetFailedAsync(taskId, errorJson).ConfigureAwait(false);
                            }
                            finally
                            {
                                // Only the side that wins TryRemove disposes cts. This prevents a
                                // double-dispose race with the default tasks/cancel handler.
                                if (_taskCancellationSources.TryRemove(taskId, out var registeredCts))
                                {
                                    registeredCts.Dispose();
                                }
                            }
                        }
                    }, CancellationToken.None);

                    return ToCreateTaskResult(taskInfo);
                }

                return await innerTaskHandler(request, cancellationToken).ConfigureAwait(false);
            };
        }

        ServerCapabilities.Tools.ListChanged = listChanged;

        SetHandler(
            RequestMethods.ToolsList,
            listToolsHandler,
            McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListToolsResult);

        SetTaskAugmentedHandler(
            RequestMethods.ToolsCall,
            callToolWithTaskHandler,
            McpJsonUtilities.JsonContext.Default.CallToolRequestParams,
            McpJsonUtilities.JsonContext.Default.CallToolResult,
            McpJsonUtilities.JsonContext.Default.CreateTaskResult);
    }

    private static CreateTaskResult ToCreateTaskResult(McpTaskInfo info) => new()
    {
        TaskId = info.TaskId,
        Status = info.Status,
        CreatedAt = info.CreatedAt,
        LastUpdatedAt = info.LastUpdatedAt,
        TimeToLive = info.TimeToLive,
        PollIntervalMs = info.PollIntervalMs,
        StatusMessage = info.StatusMessage,
        ResultType = "task",
    };

    private static GetTaskResult ToGetTaskResult(McpTaskInfo info) => info.Status switch
    {
        McpTaskStatus.Working => new WorkingTaskResult
        {
            TaskId = info.TaskId,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TimeToLive = info.TimeToLive,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
            ResultType = "complete",
        },
        McpTaskStatus.Completed => new CompletedTaskResult
        {
            TaskId = info.TaskId,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TimeToLive = info.TimeToLive,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
            Result = info.Result ?? throw new InvalidOperationException($"Task '{info.TaskId}' is completed but has no result."),
            ResultType = "complete",
        },
        McpTaskStatus.Failed => new FailedTaskResult
        {
            TaskId = info.TaskId,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TimeToLive = info.TimeToLive,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
            Error = info.Error ?? throw new InvalidOperationException($"Task '{info.TaskId}' is failed but has no error."),
            ResultType = "complete",
        },
        McpTaskStatus.Cancelled => new CancelledTaskResult
        {
            TaskId = info.TaskId,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TimeToLive = info.TimeToLive,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
            ResultType = "complete",
        },
        McpTaskStatus.InputRequired => new InputRequiredTaskResult
        {
            TaskId = info.TaskId,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TimeToLive = info.TimeToLive,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
            // McpTaskInfo.InputRequests is IReadOnlyDictionary (covers immutable store
            // implementations like InMemoryMcpTaskStore's ImmutableDictionary), while the wire
            // DTO uses IDictionary like every other Protocol type. Most concrete stores back
            // their dictionaries with a type that implements both interfaces (Dictionary,
            // ImmutableDictionary, ConcurrentDictionary), so the cast usually succeeds and we
            // only allocate a copy as a fallback.
            InputRequests = info.InputRequests is IDictionary<string, InputRequest> dict
                ? dict
                : info.InputRequests?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                    ?? new Dictionary<string, InputRequest>(),
            ResultType = "complete",
        },
        _ => throw new InvalidOperationException($"Unknown task status: {info.Status}"),
    };

    private static async ValueTask<ResultOrCreatedTask<CallToolResult>> InvokeToolAsTask(
        McpServerTool tool,
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        return await tool.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private McpRequestFilter<CallToolRequestParams, CallToolResult> BuildInitialCallToolFilter(
        McpServerPrimitiveCollection<McpServerTool>? tools) => handler =>
        async (request, cancellationToken) =>
        {
            if (request.Params?.Name is { } toolName && tools is not null &&
                tools.TryGetPrimitive(toolName, out var tool))
            {
                request.MatchedPrimitive = tool;
            }

            try
            {
                var result = await handler(request, cancellationToken).ConfigureAwait(false);
                ToolCallCompleted(request.Params?.Name ?? string.Empty, result.IsError is true);
                return result;
            }
            catch (Exception e)
            {
                // Skip logging for InputRequiredException - it's normal MRTR control flow,
                // not an error (tools throw it to signal an InputRequiredResult).
                if (!(e is OperationCanceledException && cancellationToken.IsCancellationRequested) && e is not InputRequiredException)
                {
                    ToolCallError(request.Params?.Name ?? string.Empty, e);
                }

                if ((e is OperationCanceledException && cancellationToken.IsCancellationRequested) || e is McpProtocolException || e is InputRequiredException)
                {
                    throw;
                }

                return new()
                {
                    IsError = true,
                    Content = [new TextContentBlock
                    {
                        Text = e is McpException ?
                            $"An error occurred invoking '{request.Params?.Name}': {e.Message}" :
                            $"An error occurred invoking '{request.Params?.Name}'.",
                    }],
                };
            }
        };

    private McpRequestFilter<CallToolRequestParams, ResultOrCreatedTask<CallToolResult>> BuildInitialTaskToolFilter(
        McpServerPrimitiveCollection<McpServerTool>? tools) => handler =>
        async (request, cancellationToken) =>
        {
            if (request.Params?.Name is { } toolName && tools is not null &&
                tools.TryGetPrimitive(toolName, out var tool))
            {
                request.MatchedPrimitive = tool;
            }

            try
            {
                var result = await handler(request, cancellationToken).ConfigureAwait(false);
                if (!result.IsTask)
                {
                    ToolCallCompleted(request.Params?.Name ?? string.Empty, result.Result!.IsError is true);
                }

                return result;
            }
            catch (Exception e)
            {
                // Skip logging for InputRequiredException - it's normal MRTR control flow,
                // not an error (tools throw it to signal an InputRequiredResult).
                if (!(e is OperationCanceledException && cancellationToken.IsCancellationRequested) && e is not InputRequiredException)
                {
                    ToolCallError(request.Params?.Name ?? string.Empty, e);
                }

                if ((e is OperationCanceledException && cancellationToken.IsCancellationRequested) || e is McpProtocolException || e is InputRequiredException)
                {
                    throw;
                }

                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock
                    {
                        Text = e is McpException ?
                            $"An error occurred invoking '{request.Params?.Name}': {e.Message}" :
                            $"An error occurred invoking '{request.Params?.Name}'.",
                    }],
                };
            }
        };

    private void ConfigureLogging(McpServerOptions options)
    {
        // We don't require that the handler be provided, as we always store the provided log level to the server.
        var setLoggingLevelHandler = options.Handlers.SetLoggingLevelHandler;

        // Apply filters to the handler
        if (setLoggingLevelHandler is not null)
        {
            setLoggingLevelHandler = BuildFilterPipeline(setLoggingLevelHandler, options.Filters.Request.SetLoggingLevelFilters);
        }

        ServerCapabilities.Logging = new();

        _requestHandlers.Set(
            RequestMethods.LoggingSetLevel,
            (request, jsonRpcRequest, cancellationToken) =>
            {
                if (IsJuly2026OrLaterProtocolRequest(jsonRpcRequest))
                {
                    throw new McpProtocolException(
                        $"The method '{RequestMethods.LoggingSetLevel}' is not available on protocol version '{jsonRpcRequest.Context?.ProtocolVersion ?? NegotiatedProtocolVersion}'. Use per-request _meta/{MetaKeys.LogLevel} instead.",
                        McpErrorCode.MethodNotFound);
                }

                // Store the provided level.
                if (request is not null)
                {
                    if (_loggingLevel is null)
                    {
                        Interlocked.CompareExchange(ref _loggingLevel, new(request.Level), null);
                    }

                    _loggingLevel.Value = request.Level;
                }

                // If a handler was provided, now delegate to it.
                if (setLoggingLevelHandler is not null)
                {
                    return InvokeHandlerAsync(setLoggingLevelHandler, request!, jsonRpcRequest, cancellationToken);
                }

                // Otherwise, consider it handled.
                return new ValueTask<EmptyResult>(EmptyResult.Instance);
            },
            McpJsonUtilities.JsonContext.Default.SetLevelRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);
    }

    private ValueTask<TResult> InvokeHandlerAsync<TParams, TResult>(
        McpRequestHandler<TParams, TResult> handler,
        TParams args,
        JsonRpcRequest jsonRpcRequest,
        CancellationToken cancellationToken = default)
    {
        return _servicesScopePerRequest ?
            InvokeScopedAsync(handler, args, jsonRpcRequest, cancellationToken) :
            handler(new(CreateDestinationBoundServer(jsonRpcRequest), jsonRpcRequest, args), cancellationToken);

        async ValueTask<TResult> InvokeScopedAsync(
            McpRequestHandler<TParams, TResult> handler,
            TParams args,
            JsonRpcRequest jsonRpcRequest,
            CancellationToken cancellationToken)
        {
            var scope = Services?.GetService<IServiceScopeFactory>()?.CreateAsyncScope();
            try
            {
                return await handler(
                    new RequestContext<TParams>(CreateDestinationBoundServer(jsonRpcRequest), jsonRpcRequest, args)
                    {
                        Services = scope?.ServiceProvider ?? Services,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (scope is not null)
                {
                    await scope.Value.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private DestinationBoundMcpServer CreateDestinationBoundServer(JsonRpcRequest jsonRpcRequest)
    {
        var server = new DestinationBoundMcpServer(this, jsonRpcRequest.Context?.RelatedTransport, jsonRpcRequest.Context);

        if (_mrtrContextsByRequestId.TryRemove(jsonRpcRequest.Id, out var mrtrContext))
        {
            server.ActiveMrtrContext = mrtrContext;
        }

        return server;
    }

    private void SetHandler<TParams, TResult>(
        string method,
        McpRequestHandler<TParams, TResult> handler,
        JsonTypeInfo<TParams> requestTypeInfo,
        JsonTypeInfo<TResult> responseTypeInfo)
    {
        // SEP-2549: results that carry caching hints (tools/list, prompts/list, resources/list,
        // resources/templates/list, and resources/read) declare ttlMs and cacheScope as required fields.
        // When a handler leaves them unset, fill in conservative defaults (immediately stale and not
        // shareable) so the wire form always carries the fields while preserving today's "don't cache"
        // behavior. Any value supplied by the handler or a filter is left untouched.
        if (typeof(ICacheableResult).IsAssignableFrom(typeof(TResult)))
        {
            var innerHandler = handler;
            handler = async (request, cancellationToken) =>
            {
                var result = await innerHandler(request, cancellationToken).ConfigureAwait(false);
                if (result is ICacheableResult cacheable)
                {
                    cacheable.TimeToLive ??= TimeSpan.Zero;
                    cacheable.CacheScope ??= CacheScope.Private;
                }

                return result;
            };
        }

        if (typeof(Result).IsAssignableFrom(typeof(TResult)))
        {
            var innerHandler = handler;
            handler = async (request, cancellationToken) =>
            {
                var result = await innerHandler(request, cancellationToken).ConfigureAwait(false);
                if (result is Result protocolResult && protocolResult.ResultType is null)
                {
                    protocolResult.ResultType = "complete";
                }

                return result;
            };
        }

        _requestHandlers.Set(method,
            (request, jsonRpcRequest, cancellationToken) =>
                InvokeHandlerAsync(handler, request, jsonRpcRequest, cancellationToken),
            requestTypeInfo, responseTypeInfo);
    }

    private void SetTaskAugmentedHandler<TParams, TResult>(
        string method,
        McpRequestHandler<TParams, ResultOrCreatedTask<TResult>> handler,
        JsonTypeInfo<TParams> requestTypeInfo,
        JsonTypeInfo<TResult> responseTypeInfo,
        JsonTypeInfo<CreateTaskResult> taskResultTypeInfo)
        where TResult : Result
    {
        var innerHandler = handler;
        handler = async (request, cancellationToken) =>
        {
            var result = await innerHandler(request, cancellationToken).ConfigureAwait(false);
            if (result.IsTask)
            {
                if (result.TaskCreated is { ResultType: null } taskCreated)
                {
                    taskCreated.ResultType = "task";
                }
            }
            else if (result.Result is { ResultType: null } immediateResult)
            {
                immediateResult.ResultType = "complete";
            }

            return result;
        };

        _requestHandlers.SetTaskAugmented(method,
            (request, jsonRpcRequest, cancellationToken) =>
                InvokeHandlerAsync(handler, request, jsonRpcRequest, cancellationToken),
            requestTypeInfo, responseTypeInfo, taskResultTypeInfo);
    }

    private static McpRequestHandler<TParams, TResult> BuildFilterPipeline<TParams, TResult>(
        McpRequestHandler<TParams, TResult> baseHandler,
        IList<McpRequestFilter<TParams, TResult>> filters,
        McpRequestFilter<TParams, TResult>? initialHandler = null)
    {
        var current = baseHandler;

        for (int i = filters.Count - 1; i >= 0; i--)
        {
            current = filters[i](current);
        }

        if (initialHandler is not null)
        {
            current = initialHandler(current);
        }

        return current;
    }

    // Per SEP-2663 §51, the client opts in to the tasks extension on a per-request basis
    // via the SEP-2575 capabilities envelope:
    //   _meta/io.modelcontextprotocol/clientCapabilities/extensions/io.modelcontextprotocol/tasks = {}
    private static bool HasTaskExtensionOptIn(JsonObject? meta) =>
        meta is not null &&
        meta[MetaKeys.ClientCapabilities] is JsonObject caps &&
        caps["extensions"] is JsonObject exts &&
        exts.ContainsKey(McpExtensions.Tasks);

    private JsonRpcMessageFilter BuildMessageFilterPipeline(IList<McpMessageFilter> filters)
    {
        if (filters.Count == 0)
        {
            return next => next;
        }

        return next =>
        {
            // Build the handler chain from the filters.
            // The innermost handler calls the provided 'next' delegate with the message from the context.
            McpMessageHandler baseHandler = async (context, cancellationToken) =>
            {
                await next(context.JsonRpcMessage, cancellationToken).ConfigureAwait(false);
            };

            var current = baseHandler;
            for (int i = filters.Count - 1; i >= 0; i--)
            {
                current = filters[i](current);
            }

            // Return the handler that creates a MessageContext and invokes the pipeline.
            return async (message, cancellationToken) =>
            {
                // Ensure message has a Context so Items can be shared through the pipeline
                message.Context ??= new();
                var context = new MessageContext(new DestinationBoundMcpServer(this, message.Context.RelatedTransport, message.Context), message);
                await current(context, cancellationToken).ConfigureAwait(false);
            };
        };
    }

    private void UpdateEndpointNameWithClientInfo()
    {
        if (ClientInfo is null)
        {
            return;
        }

        _endpointName = $"{_serverOnlyEndpointName}, Client ({ClientInfo.Name} {ClientInfo.Version})";
    }

    /// <summary>Maps a <see cref="LogLevel"/> to a <see cref="LoggingLevel"/>.</summary>
    internal static LoggingLevel ToLoggingLevel(LogLevel level) =>
        level switch
        {
            LogLevel.Trace => Protocol.LoggingLevel.Debug,
            LogLevel.Debug => Protocol.LoggingLevel.Debug,
            LogLevel.Information => Protocol.LoggingLevel.Info,
            LogLevel.Warning => Protocol.LoggingLevel.Warning,
            LogLevel.Error => Protocol.LoggingLevel.Error,
            LogLevel.Critical => Protocol.LoggingLevel.Critical,
            _ => Protocol.LoggingLevel.Emergency,
        };

    /// <summary>
    /// Checks whether the negotiated protocol version enables MRTR per SEP-2322 (first available in the
    /// 2026-07-28 revision). MRTR rides on the 2026-07-28 revision, so this is the MRTR-meaning alias of
    /// <see cref="McpSession.IsJuly2026OrLaterProtocol"/> - use it at the input-required/handler-suspension
    /// sites where the intent is "the client understands <see cref="InputRequiredResult"/>" rather than
    /// "the peer speaks the 2026-07-28 or later revision".
    /// </summary>
    internal bool ClientSupportsMrtr() => IsJuly2026OrLaterProtocol();

    /// <summary>
    /// Returns <see langword="true"/> when the session is stateful - the same server instance handles
    /// subsequent requests on the same session. The legacy backcompat resolver in
    /// <see cref="InvokeWithInputRequiredResultHandlingAsync"/> needs a stateful session so it can send
    /// <c>elicitation/create</c> / <c>sampling/createMessage</c> / <c>roots/list</c> to the client and
    /// retry the handler with the responses.
    /// </summary>
    internal bool HasStatefulTransport() =>
        _sessionTransport is not StreamableHttpServerTransport { Stateless: true };

    /// <summary>
    /// Returns <see langword="true"/> when the given request was negotiated under the 2026-07-28 or later protocol
    /// revision, derived from the per-request <c>_meta</c>/<c>MCP-Protocol-Version</c> value (so it works
    /// for requests over stateless HTTP) and falling back to the session-negotiated version.
    /// Used to gate the SEP-2663 Tasks extension, which only interoperates on the 2026-07-28 revision.
    /// </summary>
    private bool IsJuly2026OrLaterProtocolRequest(JsonRpcRequest? request) =>
        IsJuly2026OrLaterProtocolRequest(request?.Context);

    /// <inheritdoc cref="IsJuly2026OrLaterProtocolRequest(JsonRpcRequest?)"/>
    internal bool IsJuly2026OrLaterProtocolRequest(JsonRpcMessageContext? requestContext) =>
        McpProtocolVersions.IsJuly2026OrLaterProtocolVersion(
            requestContext?.ProtocolVersion ?? NegotiatedProtocolVersion);

    /// <inheritdoc />
    public override bool IsMrtrSupported => ClientSupportsMrtr() || HasStatefulTransport();

    /// <summary>
    /// Invokes a handler and catches <see cref="InputRequiredException"/> to convert it to an
    /// <see cref="InputRequiredResult"/> JSON response. When MRTR is negotiated or the server is stateless,
    /// the result is serialized directly. Otherwise, input requests are resolved via standard JSON-RPC
    /// calls (elicitation, sampling, roots) and the handler is retried with the responses - allowing
    /// MRTR-native tools to work transparently with clients that don't support MRTR.
    /// </summary>
    private async Task<JsonNode?> InvokeWithInputRequiredResultHandlingAsync(
        Func<JsonRpcRequest, CancellationToken, Task<JsonNode?>> handler,
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        const int MaxRetries = 10;

        for (int retry = 0; ; retry++)
        {
            try
            {
                return await handler(request, cancellationToken).ConfigureAwait(false);
            }
            catch (InputRequiredException ex)
            {
                // If the client natively supports MRTR, serialize and return directly -
                // the client will drive the retry loop.
                if (ClientSupportsMrtr())
                {
                    return SerializeInputRequiredResult(ex.Result);
                }

                // In stateless mode without MRTR, the server can't resolve input requests via
                // JSON-RPC (no persistent session for server-to-client requests), and the client
                // won't recognize the InputRequiredResult. This is the one unsupported configuration.
                if (!HasStatefulTransport())
                {
                    throw new McpException(
                        "A tool handler returned an incomplete result, but the server is stateless and the client does not support MRTR. " +
                        "MRTR-native tools require either an MRTR-capable client or a stateful server for backward-compatible resolution.", ex);
                }

                // Backcompat: resolve input requests via standard JSON-RPC calls and retry the handler.
                if (ex.Result.InputRequests is not { Count: > 0 } inputRequests)
                {
                    throw new McpException(
                        "A tool handler returned an incomplete result without input requests, and the client does not support MRTR.", ex);
                }

                if (retry >= MaxRetries)
                {
                    throw new McpException(
                        $"MRTR-native tool exceeded {MaxRetries} retry rounds without completing.", ex);
                }

                // Resolve each input request by sending the corresponding JSON-RPC call to the client.
                // Route the outgoing requests via the same DestinationBoundMcpServer used for normal tool
                // handlers, so they go through the POST's response stream (RelatedTransport) rather than
                // the session-level transport. Without this, the messages can race with the client's GET
                // stream startup and be silently dropped by StreamableHttpServerTransport.SendMessageAsync
                // when no GET request has arrived yet.
                var destinationServer = CreateDestinationBoundServer(request);
                var inputResponses = await ResolveInputRequestsAsync(destinationServer, inputRequests, cancellationToken).ConfigureAwait(false);

                // Reconstruct request params with inputResponses and requestState for the retry.
                var paramsObj = request.Params?.DeepClone() as JsonObject ?? new JsonObject();
                paramsObj["inputResponses"] = JsonSerializer.SerializeToNode(
                    (IDictionary<string, InputResponse>)inputResponses, McpJsonUtilities.JsonContext.Default.IDictionaryStringInputResponse);

                if (ex.Result.RequestState is { } requestState)
                {
                    paramsObj["requestState"] = requestState;
                }
                else
                {
                    // Strip any stale requestState carried over from the previous round's clone so
                    // the next tool invocation doesn't see a continuation token the current round is not using.
                    paramsObj.Remove("requestState");
                }

                request = new JsonRpcRequest
                {
                    Id = request.Id,
                    Method = request.Method,
                    Params = paramsObj,
                    Context = request.Context,
                };
            }
        }
    }

    /// <summary>
    /// Resolves a batch of MRTR input requests concurrently by dispatching each as a standard
    /// JSON-RPC request to the client. The requests are routed via <paramref name="destinationServer"/>
    /// so they go out through the POST's response stream (matching the behavior of tool-initiated
    /// server-to-client requests like <c>server.SampleAsync</c>) and avoid racing with the client's
    /// GET stream startup. On the first failure all remaining handlers are cancelled so user-facing
    /// flows (sampling/elicitation prompts) don't keep running once the caller has given up, and
    /// exceptions from late-completing tasks are observed before the original exception is rethrown.
    /// </summary>
    private static async Task<IDictionary<string, InputResponse>> ResolveInputRequestsAsync(
        McpServer destinationServer,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var keyed = new (string Key, Task<InputResponse> Task)[inputRequests.Count];
        int i = 0;
        foreach (var kvp in inputRequests)
        {
            keyed[i++] = (kvp.Key, ResolveInputRequestAsync(destinationServer, kvp.Value, linkedCts.Token));
        }

        try
        {
            await Task.WhenAll(Array.ConvertAll(keyed, k => k.Task)).ConfigureAwait(false);
        }
        catch
        {
            linkedCts.Cancel();
            try
            {
                await Task.WhenAll(Array.ConvertAll(keyed, k => k.Task)).ConfigureAwait(false);
            }
            catch
            {
                // Observed; the original exception is the one we want to surface.
            }
            throw;
        }

        var responses = new Dictionary<string, InputResponse>(keyed.Length);
        foreach (var (key, task) in keyed)
        {
            responses[key] = task.Result;
        }
        return responses;
    }

    /// <summary>
    /// Resolves a single MRTR <see cref="InputRequest"/> by dispatching it as a standard JSON-RPC
    /// request to the client via <paramref name="destinationServer"/>. This is the server-side mirror
    /// of the client's input resolution logic, used for backward compatibility when the client doesn't
    /// support MRTR.
    /// </summary>
    private static async Task<InputResponse> ResolveInputRequestAsync(McpServer destinationServer, InputRequest inputRequest, CancellationToken cancellationToken)
    {
        switch (inputRequest.Method)
        {
            case RequestMethods.ElicitationCreate:
                var elicitParams = inputRequest.ElicitationParams
                    ?? throw new McpException("Failed to deserialize elicitation parameters from MRTR input request.");
                var elicitResult = await destinationServer.ElicitAsync(elicitParams, cancellationToken).ConfigureAwait(false);
                return InputResponse.FromElicitResult(elicitResult);

            case RequestMethods.SamplingCreateMessage:
                var samplingParams = inputRequest.SamplingParams
                    ?? throw new McpException("Failed to deserialize sampling parameters from MRTR input request.");
                var samplingResult = await destinationServer.SampleAsync(samplingParams, cancellationToken).ConfigureAwait(false);
                return InputResponse.FromSamplingResult(samplingResult);

            case RequestMethods.RootsList:
                var rootsParams = inputRequest.RootsParams ?? new ListRootsRequestParams();
                var rootsResult = await destinationServer.RequestRootsAsync(rootsParams, cancellationToken).ConfigureAwait(false);
                return InputResponse.FromRootsResult(rootsResult);

            default:
                throw new McpException($"Unsupported input request method: '{inputRequest.Method}'.");
        }
    }

    private static JsonNode? SerializeInputRequiredResult(InputRequiredResult inputRequiredResult) =>
        JsonSerializer.SerializeToNode(inputRequiredResult, McpJsonUtilities.JsonContext.Default.InputRequiredResult);

    /// <summary>
    /// Wraps MRTR-eligible request handlers so that when a handler calls ElicitAsync/SampleAsync/RequestRootsAsync,
    /// an <see cref="InputRequiredResult"/> is returned early and the handler is suspended until the retry arrives.
    /// </summary>
    private void ConfigureMrtr()
    {
        // Wrap all methods that may trigger MRTR (server calling ElicitAsync/SampleAsync/RequestRootsAsync
        // during handler execution). These methods may produce InputRequiredResult if the handler needs input.
        WrapHandlerWithMrtr(RequestMethods.ToolsCall);
        WrapHandlerWithMrtr(RequestMethods.PromptsGet);
        WrapHandlerWithMrtr(RequestMethods.ResourcesRead);
    }

    /// <summary>
    /// Replaces an existing request handler entry with an MRTR-aware wrapper that supports
    /// handler suspension and <see cref="InputRequiredResult"/> responses.
    /// </summary>
    private void WrapHandlerWithMrtr(string method)
    {
        if (!_requestHandlers.TryGetValue(method, out var originalHandler))
        {
            return;
        }

        _requestHandlers[method] = async (request, cancellationToken) =>
        {
            // Check for MRTR retry: if requestState is present, look up the continuation.
            if (request.Params is JsonObject paramsObj &&
                paramsObj.TryGetPropertyValue("requestState", out var requestStateNode) &&
                requestStateNode?.GetValueKind() == JsonValueKind.String &&
                requestStateNode.GetValue<string>() is { } requestState)
            {
                if (_mrtrContinuations.TryRemove(requestState, out var existingContinuation))
                {
                    // Implicit MRTR retry: resume the suspended handler with client responses.
                    IDictionary<string, InputResponse>? inputResponses = null;
                    if (paramsObj.TryGetPropertyValue("inputResponses", out var responsesNode) && responsesNode is not null)
                    {
                        inputResponses = JsonSerializer.Deserialize(responsesNode, McpJsonUtilities.JsonContext.Default.IDictionaryStringInputResponse);
                    }

                    var exchange = existingContinuation.PendingExchange!;
                    var nextExchangeTask = existingContinuation.MrtrContext.ResetForNextExchange(exchange);

                    if (inputResponses is not null &&
                        inputResponses.TryGetValue(exchange.Key, out var response))
                    {
                        if (!exchange.ResponseTcs.TrySetResult(response))
                        {
                            throw new McpProtocolException(
                                $"MRTR exchange '{exchange.Key}' was already completed (possibly cancelled).",
                                McpErrorCode.InternalError);
                        }
                    }
                    else
                    {
                        if (!exchange.ResponseTcs.TrySetException(
                            new McpProtocolException($"Missing input response for key '{exchange.Key}'.", McpErrorCode.InvalidParams)))
                        {
                            throw new McpProtocolException(
                                $"MRTR exchange '{exchange.Key}' was already completed (possibly cancelled).",
                                McpErrorCode.InternalError);
                        }
                    }

                    return await AwaitMrtrHandlerAsync(
                        existingContinuation.HandlerTask, existingContinuation, nextExchangeTask, cancellationToken).ConfigureAwait(false);
                }

                // Explicit MRTR retry or invalid requestState: no continuation found.
                // Fall through to the standard MRTR-aware invocation path below. The retry data
                // (inputResponses, requestState) is already in the deserialized request params
                // for low-level handlers to access, and the MrtrContext will be set up for
                // high-level handlers that call ElicitAsync/SampleAsync.
            }

            // Implicit MRTR (handler suspension across ElicitAsync/SampleAsync) emits
            // InputRequiredResult on the wire, which only 2026-07-28 clients understand,
            // and requires the same server instance to handle the retry (stateful session).
            // For all other cases - legacy clients, stateless sessions - fall through to the
            // exception-based path, which transparently resolves InputRequiredException via
            // legacy JSON-RPC requests when the client doesn't speak MRTR.
            if (!ClientSupportsMrtr() || !HasStatefulTransport())
            {
                return await InvokeWithInputRequiredResultHandlingAsync(originalHandler, request, cancellationToken).ConfigureAwait(false);
            }

            // Start a new MRTR-aware handler invocation.
            var mrtrContext = new MrtrContext();

            // Create a long-lived CTS for the handler that survives across retries.
            // The original request's combinedCts will be disposed when this lambda returns,
            // breaking the cancellation chain. This CTS keeps the handler cancellable.
            // Like Kestrel's HttpContext.RequestAborted, the CTS is never disposed - Cancel()
            // is thread-safe with itself, and not disposing avoids deadlock risks from
            // calling Cancel/Dispose inside locks or Interlocked guards.
            var handlerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Store the MrtrContext so CreateDestinationBoundServer can pick it up and set it
            // on the per-request DestinationBoundMcpServer. This is picked up synchronously
            // before any await, so the finally cleanup is safe.
            _mrtrContextsByRequestId[request.Id] = mrtrContext;
            Task<JsonNode?> handlerTask;
            try
            {
                handlerTask = originalHandler(request, handlerCts.Token);
            }
            finally
            {
                _mrtrContextsByRequestId.TryRemove(request.Id, out _);
            }

            // Wrap handler state into a continuation for lifecycle management across retries.
            var continuation = new MrtrContinuation(handlerCts, handlerTask, mrtrContext);

            // Track the handler task for lifecycle management. The observer logs unhandled
            // exceptions and decrements _mrtrInFlightCount when the handler completes,
            // mirroring how McpSessionHandler tracks in-flight handlers.
            Interlocked.Increment(ref _mrtrInFlightCount);
            _ = ObserveHandlerCompletionAsync(handlerTask);

            return await AwaitMrtrHandlerAsync(
                handlerTask, continuation, mrtrContext.InitialExchangeTask, cancellationToken).ConfigureAwait(false);
        };
    }

    /// <summary>
    /// Awaits the outcome of an MRTR-enabled handler invocation.
    /// If the handler completes, returns its result. If an exchange arrives (handler needs input),
    /// builds and returns an <see cref="InputRequiredResult"/> and stores the continuation for future retries.
    /// If the handler throws <see cref="InputRequiredException"/>, the result is returned directly
    /// without storing a continuation (explicit MRTR path).
    /// </summary>
    private async Task<JsonNode?> AwaitMrtrHandlerAsync(
        Task<JsonNode?> handlerTask,
        MrtrContinuation continuation,
        Task<MrtrExchange> exchangeTask,
        CancellationToken cancellationToken)
    {
        // Link the current request's cancellation to the handler's long-lived CTS.
        // On the initial call this is redundant (handlerCts is already linked to cancellationToken)
        // but on retries this is critical: the retry's combinedCts cancellation must flow to the handler.
        // This is how notifications/cancelled for the retry's request ID reaches the handler.
        using var registration = cancellationToken.Register(
            static state => ((MrtrContinuation)state!).CancelHandler(), continuation);

        // Race handler against MRTR exchange.
        var completedTask = await Task.WhenAny(handlerTask, exchangeTask).ConfigureAwait(false);

        if (completedTask == handlerTask)
        {
            // Handler completed - return its result, propagate its exception, or handle InputRequiredException.
            return await AwaitHandlerWithInputRequiredResultHandlingAsync(handlerTask).ConfigureAwait(false);
        }

        // Exchange arrived - handler needs input from the client (implicit MRTR path).
        var exchange = await exchangeTask.ConfigureAwait(false);

        var correlationId = Guid.NewGuid().ToString("N");
        var inputRequiredResult = new InputRequiredResult
        {
            InputRequests = new Dictionary<string, InputRequest> { [exchange.Key] = exchange.InputRequest },
            RequestState = correlationId,
        };

        // Store the continuation so the retry can resume the handler.
        continuation.PendingExchange = exchange;
        _mrtrContinuations[correlationId] = continuation;

        return SerializeInputRequiredResult(inputRequiredResult);
    }

    /// <summary>
    /// Fire-and-forget observer for an MRTR handler task. Logs unhandled exceptions at Debug
    /// level (the same exception still propagates to the request pipeline, so Debug avoids
    /// double-reporting at Error) and decrements <see cref="_mrtrInFlightCount"/> when the
    /// handler completes, following the same in-flight tracking pattern as <see cref="McpSessionHandler"/>.
    /// </summary>
    private async Task ObserveHandlerCompletionAsync(Task<JsonNode?> handlerTask)
    {
        try
        {
            await handlerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Handler cancelled - expected lifecycle event (disposal, client cancel, session shutdown).
        }
        catch (InputRequiredException)
        {
            // Explicit MRTR: handler explicitly signaling an InputRequiredResult. Not an error.
        }
        catch (Exception ex)
        {
            MrtrHandlerError(ex);
        }
        finally
        {
            if (Interlocked.Decrement(ref _mrtrInFlightCount) == 0)
            {
                _allMrtrHandlersCompleted.TrySetResult(true);
            }
        }
    }

    /// <summary>
    /// Awaits a handler task, catching <see cref="InputRequiredException"/> to convert it to an
    /// <see cref="InputRequiredResult"/> JSON response without storing a continuation.
    /// </summary>
    private static async Task<JsonNode?> AwaitHandlerWithInputRequiredResultHandlingAsync(Task<JsonNode?> handlerTask)
    {
        try
        {
            return await handlerTask.ConfigureAwait(false);
        }
        catch (InputRequiredException ex)
        {
            return SerializeInputRequiredResult(ex.Result);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "\"{ToolName}\" threw an unhandled exception.")]
    private partial void ToolCallError(string toolName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "\"{ToolName}\" completed. IsError = {IsError}.")]
    private partial void ToolCallCompleted(string toolName, bool isError);

    [LoggerMessage(Level = LogLevel.Error, Message = "GetPrompt \"{PromptName}\" threw an unhandled exception.")]
    private partial void GetPromptError(string promptName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "GetPrompt \"{PromptName}\" completed.")]
    private partial void GetPromptCompleted(string promptName);

    [LoggerMessage(Level = LogLevel.Error, Message = "ReadResource \"{ResourceUri}\" threw an unhandled exception.")]
    private partial void ReadResourceError(string resourceUri, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "ReadResource \"{ResourceUri}\" completed.")]
    private partial void ReadResourceCompleted(string resourceUri);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cancelled {Count} pending MRTR continuation(s) during session disposal.")]
    private partial void MrtrContinuationsCancelled(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "An MRTR handler threw an unhandled exception.")]
    private partial void MrtrHandlerError(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to deliver \"{NotificationMethod}\" to subscription \"{SubscriptionId}\".")]
    private partial void SubscriptionNotificationFailed(string notificationMethod, string subscriptionId, Exception exception);
}
