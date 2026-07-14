using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Client;

/// <inheritdoc/>
#pragma warning disable MCPEXP002
internal sealed partial class McpClientImpl : McpClient
{
    private static Implementation DefaultImplementation { get; } = new()
    {
        Name = AssemblyNameHelper.DefaultAssemblyName.Name ?? nameof(McpClient),
        Version = AssemblyNameHelper.DefaultAssemblyName.Version?.ToString() ?? "1.0.0",
    };

    private readonly ILogger _logger;
    private readonly ITransport _transport;
    private readonly string _endpointName;
    private readonly McpClientOptions _options;
    private readonly McpSessionHandler _sessionHandler;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, Tool> _toolCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _registeredToolNames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _cacheableConformanceWarnedMethods = new(StringComparer.Ordinal);

    private ServerCapabilities? _serverCapabilities;
    private Implementation? _serverInfo;
    private string? _serverInstructions;
    private string? _negotiatedProtocolVersion;

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClientImpl"/> class.
    /// </summary>
    /// <param name="transport">The transport to use for communication with the server.</param>
    /// <param name="endpointName">The name of the endpoint for logging and debug purposes.</param>
    /// <param name="options">Options for the client, defining protocol version and capabilities.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    internal McpClientImpl(ITransport transport, string endpointName, McpClientOptions? options, ILoggerFactory? loggerFactory)
#pragma warning restore MCPEXP002
    {
        options ??= new();

        _transport = transport;
        _endpointName = $"Client ({options.ClientInfo?.Name ?? DefaultImplementation.Name} {options.ClientInfo?.Version ?? DefaultImplementation.Version})";
        _options = options;
        _logger = loggerFactory?.CreateLogger<McpClient>() ?? NullLogger<McpClient>.Instance;

        var notificationHandlers = new NotificationHandlers();
        var requestHandlers = new RequestHandlers();

        RegisterHandlers(options, notificationHandlers, requestHandlers);

        _sessionHandler = new McpSessionHandler(
            isServer: false,
            transport,
            endpointName,
            requestHandlers,
            notificationHandlers,
            incomingMessageFilter: null,
            outgoingMessageFilter: null,
            _logger);

        ToolDiscovered = tool => _toolCache[tool.Name] = tool;
        ToolRejected = (tool, reason) => LogToolRejected(tool.Name, reason);
        ToolCacheClearing = () =>
        {
            if (_registeredToolNames.IsEmpty)
            {
                _toolCache.Clear();
                return;
            }

            // Only remove server-discovered tools; preserve manually registered tools.
            foreach (var key in _toolCache.Keys)
            {
                if (!_registeredToolNames.ContainsKey(key))
                {
                    _toolCache.TryRemove(key, out _);
                }
            }
        };
    }

    private void RegisterHandlers(McpClientOptions options, NotificationHandlers notificationHandlers, RequestHandlers requestHandlers)
    {
        McpClientHandlers handlers = options.Handlers;

        var notificationHandlersFromOptions = handlers.NotificationHandlers;
        var samplingHandler = handlers.SamplingHandler;
        var rootsHandler = handlers.RootsHandler;
        var elicitationHandler = handlers.ElicitationHandler;

        if (notificationHandlersFromOptions is not null)
        {
            notificationHandlers.RegisterRange(notificationHandlersFromOptions);
        }

        if (samplingHandler is not null)
        {
            requestHandlers.Set(
                RequestMethods.SamplingCreateMessage,
                (request, _, cancellationToken) =>
                {
                    WarnIfLegacyRequestOnMrtrSession(RequestMethods.SamplingCreateMessage);
                    return samplingHandler(
                        request,
                        request?.ProgressToken is { } token ? new TokenProgress(this, token) : NullProgress.Instance,
                        cancellationToken);
                },
                McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
                McpJsonUtilities.JsonContext.Default.CreateMessageResult);

            _options.Capabilities ??= new();
            _options.Capabilities.Sampling ??= new();
        }

        if (rootsHandler is not null)
        {
            requestHandlers.Set(
                RequestMethods.RootsList,
                (request, _, cancellationToken) =>
                {
                    WarnIfLegacyRequestOnMrtrSession(RequestMethods.RootsList);
                    return rootsHandler(request, cancellationToken);
                },
                McpJsonUtilities.JsonContext.Default.ListRootsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListRootsResult);

            _options.Capabilities ??= new();
            _options.Capabilities.Roots ??= new();
        }

        if (elicitationHandler is not null)
        {
            requestHandlers.Set(
                RequestMethods.ElicitationCreate,
                async (request, _, cancellationToken) =>
                {
                    WarnIfLegacyRequestOnMrtrSession(RequestMethods.ElicitationCreate);
                    var result = await elicitationHandler(request, cancellationToken).ConfigureAwait(false);
                    return ElicitResult.WithDefaults(request, result);
                },
                McpJsonUtilities.JsonContext.Default.ElicitRequestParams,
                McpJsonUtilities.JsonContext.Default.ElicitResult);

            _options.Capabilities ??= new();
            _options.Capabilities.Elicitation ??= new();
            if (_options.Capabilities.Elicitation.Form is null &&
                _options.Capabilities.Elicitation.Url is null)
            {
                // If both modes are null, default to form mode for backward compatibility.
                _options.Capabilities.Elicitation.Form = new();
            }
        }
    }

    /// <inheritdoc/>
    public override string? SessionId => _transport.SessionId;

    /// <inheritdoc/>
    public override string? NegotiatedProtocolVersion => _negotiatedProtocolVersion;

    /// <inheritdoc/>
    public override ServerCapabilities ServerCapabilities => _serverCapabilities ?? throw new InvalidOperationException("The client is not connected.");

    /// <inheritdoc/>
    public override Implementation ServerInfo => _serverInfo ?? throw new InvalidOperationException("The client is not connected.");

    /// <inheritdoc/>
    public override string? ServerInstructions => _serverInstructions;

    /// <inheritdoc/>
    public override Task<ClientCompletionDetails> Completion => _sessionHandler.CompletionTask;

    /// <inheritdoc/>
    private protected override int MaxConsecutiveStuckPolls => _options.MaxConsecutiveStuckPolls;

    /// <inheritdoc/>
    private protected override async ValueTask<IDictionary<string, InputResponse>> ResolveInputRequestsAsync(
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken)
    {
        // Resolve all input requests concurrently. If any fails, cancel the rest so user-facing
        // handlers (sampling/elicitation prompts) don't keep running for a request whose caller
        // has already given up, and ensure exceptions from late-completing tasks are observed.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var keyed = new (string Key, Task<InputResponse> Task)[inputRequests.Count];
        int i = 0;
        foreach (var kvp in inputRequests)
        {
            keyed[i++] = (kvp.Key, ResolveInputRequestAsync(kvp.Value, linkedCts.Token));
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

    private async Task<InputResponse> ResolveInputRequestAsync(InputRequest inputRequest, CancellationToken cancellationToken)
    {
        switch (inputRequest.Method)
        {
            case RequestMethods.SamplingCreateMessage:
                if (_options.Handlers.SamplingHandler is { } samplingHandler)
                {
                    var samplingParams = inputRequest.SamplingParams
                        ?? throw new McpException($"Failed to deserialize sampling parameters from MRTR input request.");
                    var result = await samplingHandler(
                        samplingParams,
                        samplingParams.ProgressToken is { } token ? new TokenProgress(this, token) : NullProgress.Instance,
                        cancellationToken).ConfigureAwait(false);
                    return InputResponse.FromSamplingResult(result);
                }

                throw new InvalidOperationException(
                    $"Server sent a sampling input request, but no {nameof(McpClientHandlers.SamplingHandler)} is registered.");

            case RequestMethods.ElicitationCreate:
                if (_options.Handlers.ElicitationHandler is { } elicitationHandler)
                {
                    var elicitParams = inputRequest.ElicitationParams
                        ?? throw new McpException($"Failed to deserialize elicitation parameters from MRTR input request.");
                    var result = await elicitationHandler(elicitParams, cancellationToken).ConfigureAwait(false);
                    result = ElicitResult.WithDefaults(elicitParams, result);
                    return InputResponse.FromElicitResult(result);
                }

                throw new InvalidOperationException(
                    $"Server sent an elicitation input request, but no {nameof(McpClientHandlers.ElicitationHandler)} is registered.");

            case RequestMethods.RootsList:
                if (_options.Handlers.RootsHandler is { } rootsHandler)
                {
                    // ListRootsRequest params are optional per the spec, so fall back to an empty params instance.
                    var rootsParams = inputRequest.RootsParams ?? new ListRootsRequestParams();
                    var result = await rootsHandler(rootsParams, cancellationToken).ConfigureAwait(false);
                    return InputResponse.FromRootsResult(result);
                }

                throw new InvalidOperationException(
                    $"Server sent a roots list input request, but no {nameof(McpClientHandlers.RootsHandler)} is registered.");

            default:
                throw new NotSupportedException($"Unsupported input request method: '{inputRequest.Method}'.");
        }
    }

    /// <summary>
    /// Asynchronously connects to an MCP server, establishes the transport connection, and completes the initialization handshake.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // We don't want the ConnectAsync token to cancel the message processing loop after we've successfully connected.
            // The session handler handles cancelling the loop upon its disposal.
            _ = _sessionHandler.ProcessMessagesAsync(CancellationToken.None);

            // Perform initialization sequence
            using var initializationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            initializationCts.CancelAfter(_options.InitializationTimeout);

            try
            {
                // The 2026-07-28 revision (SEP-2575) is the default: there is no initialize
                // handshake. Instead, the client calls server/discover to learn the server's
                // capabilities and then begins sending normal RPCs that carry protocolVersion /
                // clientInfo / clientCapabilities in their per-request _meta. A null ProtocolVersion
                // prefers the 2026-07-28 revision and automatically falls back to the initialize
                // handshake when the server doesn't support it. The initialize branch below runs only when
                // the caller explicitly pins a version that still supports Streamable HTTP sessions (opting out of the default).
                if (_options.ProtocolVersion is null || McpProtocolVersions.RequiresPerRequestMetadata(_options.ProtocolVersion))
                {
                    string preferredVersion = _options.ProtocolVersion ?? McpProtocolVersions.July2026ProtocolVersion;

                    DiscoverResult? discoverResult = null;
                    bool fallbackToInitialize = false;
                    IList<string>? serverSupportedVersions = null;
                    string discoverVersion = preferredVersion;

                    // Apply a probe timeout so dual-path clients don't block forever waiting for an
                    // initialize-handshake server that silently drops unknown methods (per stdio.mdx fallback rules).
                    // The probe timeout is configurable via McpClientOptions.DiscoverProbeTimeout and is
                    // always bounded by InitializationTimeout (only applied when it is the tighter bound).
                    var probeTimeout = _options.DiscoverProbeTimeout;
                    using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(initializationCts.Token);
                    if (_options.InitializationTimeout > probeTimeout)
                    {
                        probeCts.CancelAfter(probeTimeout);
                    }

                    try
                    {
                        discoverResult = await SendDiscoverAsync(discoverVersion, probeCts.Token).ConfigureAwait(false);
                    }
                    catch (UnsupportedProtocolVersionException ex)
                    {
                        // Spec-recognized SEP-2575 signal: -32022 with data.supported[]. The server is
                        // refusing our preferred version. Retry with a supported per-request metadata
                        // version if one exists; otherwise fall back to initialize with the highest
                        // mutually supported initialize-capable version.
                        serverSupportedVersions = (IList<string>)ex.Supported;
                        var retryVersion = serverSupportedVersions
                            .Where(McpProtocolVersions.PerRequestMetadataProtocolVersions.Contains)
                            .OrderByDescending(v => v, StringComparer.Ordinal)
                            .FirstOrDefault();

                        if (retryVersion is not null)
                        {
                            if (_options.ProtocolVersion is { } pinnedVersion &&
                                StringComparer.Ordinal.Compare(retryVersion, pinnedVersion) < 0)
                            {
                                throw new McpException(
                                    $"The server does not support the requested protocol version '{pinnedVersion}'. " +
                                    "Leave McpClientOptions.ProtocolVersion unset to allow automatic fallback to an older version. " +
                                    $"Server-supported versions: {string.Join(", ", serverSupportedVersions)}.");
                            }

                            discoverVersion = retryVersion;
                            discoverResult = await SendDiscoverAsync(discoverVersion, probeCts.Token).ConfigureAwait(false);
                        }
                        else
                        {
                            fallbackToInitialize = true;
                        }
                    }
                    catch (MissingRequiredClientCapabilityException)
                    {
                        // Spec-recognized SEP-2575 signal: -32021. The server rejected
                        // our capability set. Surface as-is (no fallback): the user must add capabilities.
                        throw;
                    }
                    catch (McpProtocolException ex) when (ex.ErrorCode == McpErrorCode.HeaderMismatch)
                    {
                        // Spec-recognized SEP-2575 signal: -32020. The server rejected
                        // our request envelope (e.g., the MCP-Protocol-Version HTTP header didn't match
                        // the body _meta.io.modelcontextprotocol/protocolVersion). Surface as-is (no
                        // fallback): falling back to initialize wouldn't fix a malformed envelope.
                        throw;
                    }
                    catch (McpProtocolException ex) when (
                        ex.ErrorCode == McpErrorCode.InvalidRequest &&
                        ex.Message.Contains(McpHttpHeaders.SessionId, StringComparison.Ordinal))
                    {
                        // Local transport validation: a 2026-07-28+ response must not carry HTTP session state.
                        // This is not evidence of an initialize-handshake server, so do not fall back.
                        throw;
                    }
                    catch (McpProtocolException)
                    {
                        // Per spec PR #2844, the fallback MUST NOT be keyed to a single error code.
                        // Any non-SEP-2575 JSON-RPC error from the probe indicates an initialize-handshake server.
                        // Common causes include MethodNotFound from a server that has no
                        // server/discover handler, InvalidParams from a server confused by the
                        // SEP-2575 _meta envelope, ParseError from a server that can't handle our
                        // payload shape, or any other transport-defined error. The three SEP-2575
                        // signals (-32022 UnsupportedProtocolVersion, -32021
                        // MissingRequiredClientCapability, -32020 HeaderMismatch) are caught above and
                        // never reach here.
                        fallbackToInitialize = true;
                    }
                    catch (OperationCanceledException) when (probeCts.IsCancellationRequested && !initializationCts.IsCancellationRequested)
                    {
                        // Probe timeout elapsed without a response. Per stdio.mdx fallback rules, no
                        // response within a reasonable timeout means the server requires initialize. Fall back.
                        fallbackToInitialize = true;
                    }

                    if (discoverResult is not null && !discoverResult.SupportedVersions.Contains(discoverVersion))
                    {
                        // Server is reachable and supports server/discover, but doesn't support the
                        // version we are using. Fall back to initialize with the highest
                        // mutually-supported initialize-capable version from supportedVersions[].
                        fallbackToInitialize = true;
                        serverSupportedVersions = discoverResult.SupportedVersions;
                    }

                    if (fallbackToInitialize)
                    {
                        // Reset negotiated state and try initialize.
                        _negotiatedProtocolVersion = null;
                        _sessionHandler.NegotiatedProtocolVersion = null;

                        string fallbackVersion = serverSupportedVersions?
                            .Where(McpProtocolVersions.InitializeHandshakeProtocolVersions.Contains)
                            .OrderByDescending(v => v, StringComparer.Ordinal)
                            .FirstOrDefault()
                            ?? McpProtocolVersions.November2025ProtocolVersion;

                        // A non-null ProtocolVersion is also the minimum: refuse to fall back below the
                        // explicitly requested version. String.Compare is the spec's prescribed ordering
                        // for ISO-8601 date-based versions.
                        if (_options.ProtocolVersion is { } pinnedVersion &&
                            StringComparer.Ordinal.Compare(fallbackVersion, pinnedVersion) < 0)
                        {
                            throw new McpException(
                                $"The server does not support the requested protocol version '{pinnedVersion}'. " +
                                "Leave McpClientOptions.ProtocolVersion unset to allow automatic fallback to an older version. " +
                                (serverSupportedVersions is null
                                    ? "The server appears to require the initialize handshake."
                                    : $"Server-supported versions: {string.Join(", ", serverSupportedVersions)}."));
                        }

                        await PerformInitializeHandshakeAsync(fallbackVersion, initializationCts.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            LogServerCapabilitiesReceived(_endpointName,
                                capabilities: JsonSerializer.Serialize(discoverResult!.Capabilities, McpJsonUtilities.JsonContext.Default.ServerCapabilities),
                                serverInfo: JsonSerializer.Serialize(discoverResult.ServerInfo, McpJsonUtilities.JsonContext.Default.Implementation));
                        }

                        _serverCapabilities = discoverResult!.Capabilities;
                        _serverInfo = discoverResult.ServerInfo;
                        _serverInstructions = discoverResult.Instructions;
                    }

                    async Task<DiscoverResult> SendDiscoverAsync(string protocolVersion, CancellationToken cancellationToken)
                    {
                        // Eagerly set the negotiated version so InjectRequestMetaIfNeeded recognizes us as being
                        // on a per-request metadata revision when SendRequestAsync is invoked for server/discover.
                        _negotiatedProtocolVersion = protocolVersion;
                        _sessionHandler.NegotiatedProtocolVersion = protocolVersion;

                        return await SendRequestAsync(
                            RequestMethods.ServerDiscover,
                            new DiscoverRequestParams(),
                            McpJsonUtilities.JsonContext.Default.DiscoverRequestParams,
                            McpJsonUtilities.JsonContext.Default.DiscoverResult,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    // initialize handshake. Reached only when the caller explicitly pinned a
                    // ProtocolVersion that still supports Streamable HTTP sessions (opting out of the default), so
                    // _options.ProtocolVersion is non-null here.
                    string requestProtocol = _options.ProtocolVersion ?? McpProtocolVersions.November2025ProtocolVersion;
                    await PerformInitializeHandshakeAsync(requestProtocol, initializationCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException oce) when (initializationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                LogClientInitializationTimeout(_endpointName);
                throw new TimeoutException("Initialization timed out", oce);
            }
        }
        catch (Exception e)
        {
            LogClientInitializationError(_endpointName, e);
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }

        LogClientConnected(_endpointName);
    }

    /// <summary>
    /// Performs the initialize handshake (initialize request + initialized notification),
    /// records the negotiated protocol version, and stores the server capabilities/info/instructions.
    /// </summary>
    private async Task PerformInitializeHandshakeAsync(string requestProtocol, CancellationToken cancellationToken)
    {
        var initializeResponse = await SendRequestAsync(
            RequestMethods.Initialize,
            new InitializeRequestParams
            {
                ProtocolVersion = requestProtocol,
                Capabilities = _options.Capabilities ?? new ClientCapabilities(),
                ClientInfo = _options.ClientInfo ?? DefaultImplementation,
                Meta = _options.InitializeMeta,
            },
            McpJsonUtilities.JsonContext.Default.InitializeRequestParams,
            McpJsonUtilities.JsonContext.Default.InitializeResult,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            LogServerCapabilitiesReceived(_endpointName,
                capabilities: JsonSerializer.Serialize(initializeResponse.Capabilities, McpJsonUtilities.JsonContext.Default.ServerCapabilities),
                serverInfo: JsonSerializer.Serialize(initializeResponse.ServerInfo, McpJsonUtilities.JsonContext.Default.Implementation));
        }

        _serverCapabilities = initializeResponse.Capabilities;
        _serverInfo = initializeResponse.ServerInfo;
        _serverInstructions = initializeResponse.Instructions;

        // When the user explicitly pinned a version that supports Streamable HTTP sessions, the server MUST respect it.
        // When no version was pinned, accept any supported initialize-handshake response. initialize cannot negotiate
        // the 2026-07-28 and later protocol revisions.
        bool isResponseProtocolValid;
        if (_options.ProtocolVersion is { } optionsProtocol && !McpProtocolVersions.IsJuly2026OrLaterProtocolVersion(optionsProtocol))
        {
            isResponseProtocolValid = optionsProtocol == initializeResponse.ProtocolVersion;
        }
        else
        {
            isResponseProtocolValid = McpProtocolVersions.InitializeHandshakeProtocolVersions.Contains(initializeResponse.ProtocolVersion);
        }
        if (!isResponseProtocolValid)
        {
            LogServerProtocolVersionMismatch(_endpointName, requestProtocol, initializeResponse.ProtocolVersion);
            throw new McpException($"Server protocol version mismatch. Expected {requestProtocol}, got {initializeResponse.ProtocolVersion}");
        }

        _negotiatedProtocolVersion = initializeResponse.ProtocolVersion;
        _sessionHandler.NegotiatedProtocolVersion = _negotiatedProtocolVersion;

        await this.SendNotificationAsync(
            NotificationMethods.InitializedNotification,
            new InitializedNotificationParams(),
            McpJsonUtilities.JsonContext.Default.InitializedNotificationParams,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Configures the client to use an already initialized session without performing the handshake.
    /// </summary>
    /// <param name="resumeOptions">The metadata captured from the previous session that should be applied to the resumed client.</param>
    internal void ResumeSession(ResumeClientSessionOptions resumeOptions)
    {
        Throw.IfNull(resumeOptions);
        Throw.IfNull(resumeOptions.ServerCapabilities);
        Throw.IfNull(resumeOptions.ServerInfo);

        _ = _sessionHandler.ProcessMessagesAsync(CancellationToken.None);

        _serverCapabilities = resumeOptions.ServerCapabilities;
        _serverInfo = resumeOptions.ServerInfo;
        _serverInstructions = resumeOptions.ServerInstructions;
        _negotiatedProtocolVersion = resumeOptions.NegotiatedProtocolVersion
            ?? _options.ProtocolVersion
            ?? McpProtocolVersions.November2025ProtocolVersion;

        // Update session handler with the negotiated protocol version for telemetry
        _sessionHandler.NegotiatedProtocolVersion = _negotiatedProtocolVersion;

        LogClientSessionResumed(_endpointName);
    }

    /// <inheritdoc/>
    public override void AddKnownTools(IEnumerable<Tool> tools)
    {
        Throw.IfNull(tools);

        var snapshot = tools as IReadOnlyCollection<Tool> ?? [.. tools];

        List<string>? rejections = null;
        foreach (var tool in snapshot)
        {
            Throw.IfNull(tool);

            if (!McpHeaderExtractor.ValidateToolSchema(tool, out var rejectionReason))
            {
                ToolRejected?.Invoke(tool, rejectionReason!);
                (rejections ??= []).Add($"{tool.Name}: {rejectionReason}");
            }
        }

        if (rejections is { Count: > 0 })
        {
            throw new ArgumentException(
                "One or more tools failed x-mcp-header validation: " + string.Join("; ", rejections),
                nameof(tools));
        }

        foreach (var tool in snapshot)
        {
            _registeredToolNames[tool.Name] = 0;
            _toolCache[tool.Name] = tool;
        }
    }

    /// <inheritdoc/>
    public override void RemoveKnownTools(IEnumerable<string> toolNames)
    {
        Throw.IfNull(toolNames);

        var snapshot = toolNames as IReadOnlyCollection<string> ?? [.. toolNames];

        foreach (var name in snapshot)
        {
            Throw.IfNull(name);
        }

        foreach (var name in snapshot)
        {
            _registeredToolNames.TryRemove(name, out _);
            _toolCache.TryRemove(name, out _);
        }
    }

    /// <inheritdoc/>
    public override void ClearKnownTools()
    {
        foreach (var name in _registeredToolNames.Keys)
        {
            _toolCache.TryRemove(name, out _);
        }

        _registeredToolNames.Clear();
    }

    /// <inheritdoc/>
    public override async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
    {
        // For tools/call requests, attach the cached tool definition to the message context
        // so the transport can add custom Mcp-Param-* headers based on x-mcp-header schema annotations.
        if (request.Method == RequestMethods.ToolsCall &&
            request.Params is System.Text.Json.Nodes.JsonObject paramsObjForHeaders &&
            paramsObjForHeaders.TryGetPropertyValue("name", out var nameNode) &&
            nameNode?.GetValue<string>() is { } toolName)
        {
            if (_toolCache.TryGetValue(toolName, out var tool))
            {
                request.Context ??= new();
                request.Context.Items ??= new Dictionary<string, object?>();
                request.Context.Items[McpHttpHeaders.ToolContextKey] = tool;
            }
            else if (_transport is StreamableHttpClientSessionTransport)
            {
                LogToolCacheMiss(toolName);
            }
        }

        const int maxRetries = 10;

        InjectRequestMetaIfNeeded(request);

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            JsonRpcResponse response = await _sessionHandler.SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            // Check if the result is an InputRequiredResult by looking at result_type.
            if (response.Result is JsonObject resultObj &&
                resultObj.TryGetPropertyValue("resultType", out var resultTypeNode) &&
                resultTypeNode?.GetValue<string>() is "input_required")
            {
                WarnIfInputRequiredResultOnNonMrtrSession(request.Method);

                var inputRequiredResult = JsonSerializer.Deserialize(response.Result, McpJsonUtilities.JsonContext.Default.InputRequiredResult)
                    ?? throw new JsonException("Failed to deserialize InputRequiredResult.");

                if (inputRequiredResult.InputRequests is { Count: > 0 } inputRequests)
                {
                    IDictionary<string, InputResponse> inputResponses =
                        await ResolveInputRequestsAsync(inputRequests, cancellationToken).ConfigureAwait(false);

                    // Clone the original request params and add inputResponses + requestState for the retry.
                    var paramsObj = request.Params?.DeepClone() as JsonObject ?? new JsonObject();

                    paramsObj["inputResponses"] = JsonSerializer.SerializeToNode(
                        inputResponses, McpJsonUtilities.JsonContext.Default.IDictionaryStringInputResponse);

                    if (inputRequiredResult.RequestState is { } requestState)
                    {
                        paramsObj["requestState"] = requestState;
                    }
                    else
                    {
                        // Strip any stale requestState carried over from the previous round's clone so
                        // the server doesn't see a continuation token the current round is not using.
                        paramsObj.Remove("requestState");
                    }

                    request = new JsonRpcRequest { Method = request.Method, Params = paramsObj, Context = request.Context };
                    InjectRequestMetaIfNeeded(request);
                }
                else if (inputRequiredResult.RequestState is not null)
                {
                    // No input requests but has requestState (e.g., load shedding) - just retry with state.
                    var paramsObj = request.Params?.DeepClone() as JsonObject ?? new JsonObject();
                    paramsObj["requestState"] = inputRequiredResult.RequestState;
                    paramsObj.Remove("inputResponses");

                    request = new JsonRpcRequest { Method = request.Method, Params = paramsObj, Context = request.Context };
                    InjectRequestMetaIfNeeded(request);
                }
                else
                {
                    // An input_required result carrying neither inputRequests nor requestState is
                    // malformed: there is nothing to resolve and nothing to continue, so retrying the
                    // unchanged request would just loop until maxRetries. Fail fast instead.
                    throw new McpException("Server returned an InputRequiredResult without inputRequests or requestState.");
                }

                continue; // retry with the updated request
            }

            return response;
        }

        throw new McpException($"Server returned InputRequiredResult more than {maxRetries} times.");
    }

    /// <summary>
    /// Injects the 2026-07-28 protocol's per-request <c>_meta</c> fields (protocol version, client info,
    /// client capabilities) into the request when this client negotiated the 2026-07-28 or later revision
    /// (SEP-2575). No-op on an initialize-handshake session.
    /// </summary>
    private void InjectRequestMetaIfNeeded(JsonRpcRequest request)
    {
        if (!IsJuly2026OrLaterProtocol())
        {
            return;
        }

        // Initialize is never sent on a 2026-07-28 session, but guard defensively in case a caller
        // routes it through here (e.g., during back-compat fallback negotiation).
        if (request.Method == RequestMethods.Initialize)
        {
            return;
        }

        McpSessionHandler.InjectRequestMeta(
            request,
            _negotiatedProtocolVersion!,
            _options.ClientInfo ?? DefaultImplementation,
            _options.Capabilities ?? new ClientCapabilities());
    }

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

        await _sessionHandler.DisposeAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);

        // After disposal, the channel writer is complete but ProcessMessagesCoreAsync
        // may have been cancelled with unread items still buffered. ChannelReader.Completion
        // only resolves once all items are consumed, so drain remaining items.
        while (_transport.MessageReader.TryRead(out var _));

        // Then ensure all work has quiesced.
        await Completion.ConfigureAwait(false);
    }

    /// <summary>Logs a warning if the session negotiated MRTR but the server sent a legacy JSON-RPC request.</summary>
    private void WarnIfLegacyRequestOnMrtrSession(string method)
    {
        if (IsJuly2026OrLaterProtocol())
        {
            LogLegacyRequestOnMrtrSession(_endpointName, method);
        }
    }

    /// <summary>Logs a warning if the session did not negotiate MRTR but the server sent an InputRequiredResult.</summary>
    private void WarnIfInputRequiredResultOnNonMrtrSession(string method)
    {
        if (!IsJuly2026OrLaterProtocol())
        {
            LogInputRequiredResultOnNonMrtrSession(_endpointName, method, _negotiatedProtocolVersion);
        }
    }

    /// <summary>
    /// Logs a warning (never throws) when a server that negotiated the 2026-07-28 (or later) protocol version
    /// omits the SEP-2549 <c>ttlMs</c>/<c>cacheScope</c> fields, which are required on cacheable results for
    /// those versions. The warning is emitted at most once per method per session so that paginated listings do
    /// not produce one warning per page.
    /// </summary>
    private protected override void ValidateCacheableResult(string method, ICacheableResult result)
    {
        if (!IsJuly2026OrLaterProtocol())
        {
            return;
        }

        bool missingTtl = result.TimeToLive is null;
        bool missingScope = result.CacheScope is null;
        if ((missingTtl || missingScope) && _cacheableConformanceWarnedMethods.TryAdd(method, 0))
        {
            string missingFields =
                missingTtl && missingScope ? "ttlMs, cacheScope" :
                missingTtl ? "ttlMs" :
                "cacheScope";
            LogCacheableResultMissingRequiredFields(_endpointName, method, missingFields, _negotiatedProtocolVersion);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} received '{Method}' result missing required SEP-2549 field(s) '{MissingFields}' from a server that negotiated protocol version '{ProtocolVersion}'. The server may not be spec-compliant.")]
    private partial void LogCacheableResultMissingRequiredFields(string endpointName, string method, string missingFields, string? protocolVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} received legacy '{Method}' JSON-RPC request on session that negotiated MRTR. The server should use InputRequiredResult instead of sending direct requests.")]
    private partial void LogLegacyRequestOnMrtrSession(string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} received InputRequiredResult for '{Method}' on session that did not negotiate MRTR (protocol version '{ProtocolVersion}'). The server may not be spec-compliant.")]
    private partial void LogInputRequiredResultOnNonMrtrSession(string endpointName, string method, string? protocolVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} client received server '{ServerInfo}' capabilities: '{Capabilities}'.")]
    private partial void LogServerCapabilitiesReceived(string endpointName, string capabilities, string serverInfo);

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} client initialization error.")]
    private partial void LogClientInitializationError(string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} client initialization timed out.")]
    private partial void LogClientInitializationTimeout(string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} client protocol version mismatch with server. Expected '{Expected}', received '{Received}'.")]
    private partial void LogServerProtocolVersionMismatch(string endpointName, string expected, string received);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} client created and connected.")]
    private partial void LogClientConnected(string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} client resumed existing session.")]
    private partial void LogClientSessionResumed(string endpointName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool '{ToolName}' not found in cache during tools/call. Mcp-Param-* headers will not be sent. Call AddKnownTools or ListToolsAsync to populate the cache.")]
    private partial void LogToolCacheMiss(string toolName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool '{ToolName}' excluded from tools/list: {Reason}")]
    private partial void LogToolRejected(string toolName, string reason);
}
