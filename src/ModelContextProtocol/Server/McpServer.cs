using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Shared;
using ModelContextProtocol.Utils;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <inheritdoc />
internal sealed class McpServer : McpJsonRpcEndpoint, IMcpServer
{
    private readonly EventHandler? _toolsChangedDelegate;
    private readonly EventHandler? _promptsChangedDelegate;

    private ITransport _sessionTransport;
    private string _endpointName;

    /// <summary>
    /// Creates a new instance of <see cref="McpServer"/>.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established session.</param>
    /// <param name="options">Configuration options for this server, including capabilities.
    /// Make sure to accurately reflect exactly what capabilities the server supports and does not support.</param>
    /// <param name="loggerFactory">Logger factory to use for logging</param>
    /// <param name="serviceProvider">Optional service provider to use for dependency injection</param>
    /// <exception cref="McpServerException"></exception>
    public McpServer(ITransport transport, McpServerOptions options, ILoggerFactory? loggerFactory, IServiceProvider? serviceProvider)
        : base(loggerFactory)
    {
        Throw.IfNull(transport);
        Throw.IfNull(options);

        _sessionTransport = transport;
        ServerOptions = options;
        Services = serviceProvider;
        _endpointName = $"Server ({options.ServerInfo.Name} {options.ServerInfo.Version})";

        _toolsChangedDelegate = delegate
        {
            _ = SendMessageAsync(new JsonRpcNotification()
            {
                Method = NotificationMethods.ToolListChangedNotification,
            });
        };
        _promptsChangedDelegate = delegate
        {
            _ = SendMessageAsync(new JsonRpcNotification()
            {
                Method = NotificationMethods.PromptListChangedNotification,
            });
        };

        AddNotificationHandler(NotificationMethods.InitializedNotification, _ =>
        {
            if (ServerOptions.Capabilities?.Tools?.ToolCollection is { } tools)
            {
                tools.Changed += _toolsChangedDelegate;
            }

            if (ServerOptions.Capabilities?.Prompts?.PromptCollection is { } prompts)
            {
                prompts.Changed += _promptsChangedDelegate;
            }

            return Task.CompletedTask;
        });

        SetToolsHandler(options);
        SetInitializeHandler(options);
        SetCompletionHandler(options);
        SetPingHandler();
        SetPromptsHandler(options);
        SetResourcesHandler(options);
        SetSetLoggingLevelHandler(options);
    }

    public ServerCapabilities? ServerCapabilities { get; set; }

    /// <inheritdoc />
    public ClientCapabilities? ClientCapabilities { get; set; }

    /// <inheritdoc />
    public Implementation? ClientInfo { get; set; }

    /// <inheritdoc />
    public McpServerOptions ServerOptions { get; }

    /// <inheritdoc />
    public IServiceProvider? Services { get; }

    /// <inheritdoc />
    public override string EndpointName => _endpointName;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Start processing messages
            StartSession(_sessionTransport, fullSessionCancellationToken: cancellationToken);
            await MessageProcessingTask.ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public override async ValueTask DisposeUnsynchronizedAsync()
    {
        if (ServerOptions.Capabilities?.Tools?.ToolCollection is { } tools)
        {
            tools.Changed -= _toolsChangedDelegate;
        }

        if (ServerOptions.Capabilities?.Prompts?.PromptCollection is { } prompts)
        {
            prompts.Changed -= _promptsChangedDelegate;
        }

        await base.DisposeUnsynchronizedAsync().ConfigureAwait(false);
    }

    private void SetPingHandler()
    {
        SetRequestHandler<JsonNode, PingResult>(RequestMethods.Ping,
            (request, _) => Task.FromResult(new PingResult()));
    }

    private void SetInitializeHandler(McpServerOptions options)
    {
        SetRequestHandler<InitializeRequestParams, InitializeResult>(RequestMethods.Initialize,
            (request, _) =>
            {
                ClientCapabilities = request?.Capabilities ?? new();
                ClientInfo = request?.ClientInfo;

                // Use the ClientInfo to update the session EndpointName for logging.
                _endpointName = $"{_endpointName}, Client ({ClientInfo?.Name} {ClientInfo?.Version})";
                GetSessionOrThrow().EndpointName = _endpointName;

                return Task.FromResult(new InitializeResult()
                {
                    ProtocolVersion = options.ProtocolVersion,
                    Instructions = options.ServerInstructions,
                    ServerInfo = options.ServerInfo,
                    Capabilities = ServerCapabilities ?? new(),
                });
            });
    }

    private void SetCompletionHandler(McpServerOptions options)
    {
        // This capability is not optional, so return an empty result if there is no handler.
        SetRequestHandler<CompleteRequestParams, CompleteResult>(RequestMethods.CompletionComplete,
            options.GetCompletionHandler is { } handler ?
                (request, ct) => handler(new(this, request), ct) :
                (request, ct) => Task.FromResult(new CompleteResult() { Completion = new() { Values = [], Total = 0, HasMore = false } }));
    }

    private void SetResourcesHandler(McpServerOptions options)
    {
        if (options.Capabilities?.Resources is not { } resourcesCapability)
        {
            return;
        }

        var listResourcesHandler = resourcesCapability.ListResourcesHandler;
        var listResourceTemplatesHandler = resourcesCapability.ListResourceTemplatesHandler;

        if ((listResourcesHandler is not { } && listResourceTemplatesHandler is not { }) ||
            resourcesCapability.ReadResourceHandler is not { } readResourceHandler)
        {
            throw new McpServerException("Resources capability was enabled, but ListResources and/or ReadResource handlers were not specified.");
        }

        listResourcesHandler ??= (static (_, _) => Task.FromResult(new ListResourcesResult()));

        SetRequestHandler<ListResourcesRequestParams, ListResourcesResult>(RequestMethods.ResourcesList, (request, ct) => listResourcesHandler(new(this, request), ct));
        SetRequestHandler<ReadResourceRequestParams, ReadResourceResult>(RequestMethods.ResourcesRead, (request, ct) => readResourceHandler(new(this, request), ct));

        listResourceTemplatesHandler ??= (static (_, _) => Task.FromResult(new ListResourceTemplatesResult()));
        SetRequestHandler<ListResourceTemplatesRequestParams, ListResourceTemplatesResult>(RequestMethods.ResourcesTemplatesList, (request, ct) => listResourceTemplatesHandler(new(this, request), ct));

        if (resourcesCapability.Subscribe is not true)
        {
            return;
        }

        var subscribeHandler = resourcesCapability.SubscribeToResourcesHandler;
        var unsubscribeHandler = resourcesCapability.UnsubscribeFromResourcesHandler;
        if (subscribeHandler is null || unsubscribeHandler is null)
        {
            throw new McpServerException("Resources capability was enabled with subscribe support, but SubscribeToResources and/or UnsubscribeFromResources handlers were not specified.");
        }

        SetRequestHandler<SubscribeRequestParams, EmptyResult>(RequestMethods.ResourcesSubscribe, (request, ct) => subscribeHandler(new(this, request), ct));
        SetRequestHandler<UnsubscribeRequestParams, EmptyResult>(RequestMethods.ResourcesUnsubscribe, (request, ct) => unsubscribeHandler(new(this, request), ct));
    }

    private void SetPromptsHandler(McpServerOptions options)
    {
        PromptsCapability? promptsCapability = options.Capabilities?.Prompts;
        var listPromptsHandler = promptsCapability?.ListPromptsHandler;
        var getPromptHandler = promptsCapability?.GetPromptHandler;
        var prompts = promptsCapability?.PromptCollection;

        if (listPromptsHandler is null != getPromptHandler is null)
        {
            throw new McpServerException("ListPrompts and GetPrompt handlers should be specified together.");
        }

        // Handle prompts provided via DI.
        if (prompts is { IsEmpty: false })
        {
            // Synthesize the handlers, making sure a PromptsCapability is specified.
            var originalListPromptsHandler = listPromptsHandler;
            listPromptsHandler = async (request, cancellationToken) =>
            {
                ListPromptsResult result = originalListPromptsHandler is not null ?
                    await originalListPromptsHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    result.Prompts.AddRange(prompts.Select(t => t.ProtocolPrompt));
                }

                return result;
            };

            var originalGetPromptHandler = getPromptHandler;
            getPromptHandler = (request, cancellationToken) =>
            {
                if (request.Params is null ||
                    !prompts.TryGetPrimitive(request.Params.Name, out var prompt))
                {
                    if (originalGetPromptHandler is not null)
                    {
                        return originalGetPromptHandler(request, cancellationToken);
                    }

                    throw new McpServerException($"Unknown prompt '{request.Params?.Name}'");
                }

                return prompt.GetAsync(request, cancellationToken);
            };

            ServerCapabilities = new()
            {
                Experimental = options.Capabilities?.Experimental,
                Logging = options.Capabilities?.Logging,
                Tools = options.Capabilities?.Tools,
                Resources = options.Capabilities?.Resources,
                Prompts = new()
                {
                    ListPromptsHandler = listPromptsHandler,
                    GetPromptHandler = getPromptHandler,
                    PromptCollection = prompts,
                    ListChanged = true,
                }
            };
        }
        else
        {
            ServerCapabilities = options.Capabilities;

            if (promptsCapability is null)
            {
                // No prompts, and no prompts capability was declared, so nothing to do.
                return;
            }

            // Make sure the handlers are provided if the capability is enabled.
            if (listPromptsHandler is null || getPromptHandler is null)
            {
                throw new McpServerException("ListPrompts and/or GetPrompt handlers were not specified but the Prompts capability was enabled.");
            }
        }

        SetRequestHandler<ListPromptsRequestParams, ListPromptsResult>(RequestMethods.PromptsList, (request, ct) => listPromptsHandler(new(this, request), ct));
        SetRequestHandler<GetPromptRequestParams, GetPromptResult>(RequestMethods.PromptsGet, (request, ct) => getPromptHandler(new(this, request), ct));
    }

    private void SetToolsHandler(McpServerOptions options)
    {
        ToolsCapability? toolsCapability = options.Capabilities?.Tools;
        var listToolsHandler = toolsCapability?.ListToolsHandler;
        var callToolHandler = toolsCapability?.CallToolHandler;
        var tools = toolsCapability?.ToolCollection;

        if (listToolsHandler is null != callToolHandler is null)
        {
            throw new McpServerException("ListTools and CallTool handlers should be specified together.");
        }

        // Handle tools provided via DI.
        if (tools is { IsEmpty: false })
        {
            // Synthesize the handlers, making sure a ToolsCapability is specified.
            var originalListToolsHandler = listToolsHandler;
            listToolsHandler = async (request, cancellationToken) =>
            {
                ListToolsResult result = originalListToolsHandler is not null ?
                    await originalListToolsHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    result.Tools.AddRange(tools.Select(t => t.ProtocolTool));
                }

                return result;
            };

            var originalCallToolHandler = callToolHandler;
            callToolHandler = (request, cancellationToken) =>
            {
                if (request.Params is null ||
                    !tools.TryGetPrimitive(request.Params.Name, out var tool))
                {
                    if (originalCallToolHandler is not null)
                    {
                        return originalCallToolHandler(request, cancellationToken);
                    }

                    throw new McpServerException($"Unknown tool '{request.Params?.Name}'");
                }

                return tool.InvokeAsync(request, cancellationToken);
            };

            ServerCapabilities = new()
            {
                Experimental = options.Capabilities?.Experimental,
                Logging = options.Capabilities?.Logging,
                Prompts = options.Capabilities?.Prompts,
                Resources = options.Capabilities?.Resources,
                Tools = new()
                {
                    ListToolsHandler = listToolsHandler,
                    CallToolHandler = callToolHandler,
                    ToolCollection = tools,
                    ListChanged = true,
                }
            };
        }
        else
        {
            ServerCapabilities = options.Capabilities;

            if (toolsCapability is null)
            {
                // No tools, and no tools capability was declared, so nothing to do.
                return;
            }

            // Make sure the handlers are provided if the capability is enabled.
            if (listToolsHandler is null || callToolHandler is null)
            {
                throw new McpServerException("ListTools and/or CallTool handlers were not specified but the Tools capability was enabled.");
            }
        }

        SetRequestHandler<ListToolsRequestParams, ListToolsResult>(RequestMethods.ToolsList, (request, ct) => listToolsHandler(new(this, request), ct));
        SetRequestHandler<CallToolRequestParams, CallToolResponse>(RequestMethods.ToolsCall, (request, ct) => callToolHandler(new(this, request), ct));
    }

    private void SetSetLoggingLevelHandler(McpServerOptions options)
    {
        if (options.Capabilities?.Logging is not { } loggingCapability)
        {
            return;
        }

        if (loggingCapability.SetLoggingLevelHandler is not { } setLoggingLevelHandler)
        {
            throw new McpServerException("Logging capability was enabled, but SetLoggingLevelHandler was not specified.");
        }

        SetRequestHandler<SetLevelRequestParams, EmptyResult>(RequestMethods.LoggingSetLevel, (request, ct) => setLoggingLevelHandler(new(this, request), ct));
    }
}