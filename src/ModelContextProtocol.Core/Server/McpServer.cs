using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an instance of a Model Context Protocol (MCP) server that connects to and communicates with an MCP client.
/// </summary>
public abstract class McpServer : McpSession
{
    /// <summary>
    /// Gets the capabilities supported by the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These capabilities are established during the initialization handshake and indicate
    /// which features the client supports, such as sampling, roots, and other
    /// protocol-specific functionality.
    /// </para>
    /// <para>
    /// Server implementations can check these capabilities to determine which features
    /// are available when interacting with the client.
    /// </para>
    /// </remarks>
    public abstract ClientCapabilities? ClientCapabilities { get; }

    /// <summary>
    /// Gets the version and implementation information of the connected client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property contains identification information about the client that has connected to this server,
    /// including its name and version. This information is provided by the client during initialization.
    /// </para>
    /// <para>
    /// Server implementations can use this information for logging, tracking client versions, 
    /// or implementing client-specific behaviors.
    /// </para>
    /// </remarks>
    public abstract Implementation? ClientInfo { get; }

    /// <summary>
    /// Gets the options used to construct this server.
    /// </summary>
    /// <remarks>
    /// These options define the server's capabilities, protocol version, and other configuration
    /// settings that were used to initialize the server.
    /// </remarks>
    public abstract McpServerOptions ServerOptions { get; }

    /// <summary>
    /// Gets the service provider for the server.
    /// </summary>
    public abstract IServiceProvider? Services { get; }

    /// <summary>Gets the last logging level set by the client, or <see langword="null"/> if it's never been set.</summary>
    public abstract LoggingLevel? LoggingLevel { get; }

    /// <summary>
    /// Runs the server, listening for and handling client requests.
    /// </summary>
    public abstract Task RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new instance of an <see cref="McpServer"/>.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established MCP session.</param>
    /// <param name="serverOptions">Configuration options for this server, including capabilities. </param>
    /// <param name="loggerFactory">Logger factory to use for logging. If null, logging will be disabled.</param>
    /// <param name="serviceProvider">Optional service provider to create new instances of tools and other dependencies.</param>
    /// <returns>An <see cref="McpServer"/> instance that should be disposed when no longer needed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/>.</exception>
    public static McpServer Create(
        ITransport transport,
        McpServerOptions serverOptions,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null)
    {
        Throw.IfNull(transport);
        Throw.IfNull(serverOptions);

        return new McpServerImpl(transport, serverOptions, loggerFactory, serviceProvider);
    }

    /// <summary>
    /// Requests to sample an LLM via the client using the specified request parameters.
    /// </summary>
    /// <param name="request">The parameters for the sampling request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the sampling result from the client.</returns>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    public ValueTask<CreateMessageResult> SampleAsync(
        CreateMessageRequestParams request, CancellationToken cancellationToken = default)
    {
        ThrowIfSamplingUnsupported();

        return SendRequestAsync(
            RequestMethods.SamplingCreateMessage,
            request,
            McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
            McpJsonUtilities.JsonContext.Default.CreateMessageResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests to sample an LLM via the client using the provided chat messages and options.
    /// </summary>
    /// <param name="messages">The messages to send as part of the request.</param>
    /// <param name="options">The options to use for the request, including model parameters and constraints.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the chat response from the model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    public async Task<ChatResponse> SampleAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = default, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(messages);

        StringBuilder? systemPrompt = null;

        if (options?.Instructions is { } instructions)
        {
            (systemPrompt ??= new()).Append(instructions);
        }

        List<SamplingMessage> samplingMessages = [];
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (systemPrompt is null)
                {
                    systemPrompt = new();
                }
                else
                {
                    systemPrompt.AppendLine();
                }

                systemPrompt.Append(message.Text);
                continue;
            }

            if (message.Role == ChatRole.User || message.Role == ChatRole.Assistant)
            {
                Role role = message.Role == ChatRole.User ? Role.User : Role.Assistant;

                foreach (var content in message.Contents)
                {
                    switch (content)
                    {
                        case TextContent textContent:
                            samplingMessages.Add(new()
                            {
                                Role = role,
                                Content = new TextContentBlock { Text = textContent.Text },
                            });
                            break;

                        case DataContent dataContent when dataContent.HasTopLevelMediaType("image") || dataContent.HasTopLevelMediaType("audio"):
                            samplingMessages.Add(new()
                            {
                                Role = role,
                                Content = dataContent.HasTopLevelMediaType("image") ?
                                    new ImageContentBlock
                                    {
                                        MimeType = dataContent.MediaType,
                                        Data = dataContent.Base64Data.ToString(),
                                    } :
                                    new AudioContentBlock
                                    {
                                        MimeType = dataContent.MediaType,
                                        Data = dataContent.Base64Data.ToString(),
                                    },
                            });
                            break;
                    }
                }
            }
        }

        ModelPreferences? modelPreferences = null;
        if (options?.ModelId is { } modelId)
        {
            modelPreferences = new() { Hints = [new() { Name = modelId }] };
        }

        var result = await SampleAsync(new()
            {
                Messages = samplingMessages,
                MaxTokens = options?.MaxOutputTokens,
                StopSequences = options?.StopSequences?.ToArray(),
                SystemPrompt = systemPrompt?.ToString(),
                Temperature = options?.Temperature,
                ModelPreferences = modelPreferences,
            }, cancellationToken).ConfigureAwait(false);

        AIContent? responseContent = result.Content.ToAIContent();

        return new(new ChatMessage(result.Role is Role.User ? ChatRole.User : ChatRole.Assistant, responseContent is not null ? [responseContent] : []))
        {
            ModelId = result.Model,
            FinishReason = result.StopReason switch
            {
                "maxTokens" => ChatFinishReason.Length,
                "endTurn" or "stopSequence" or _ => ChatFinishReason.Stop,
            }
        };
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> wrapper that can be used to send sampling requests to the client.
    /// </summary>
    /// <returns>The <see cref="IChatClient"/> that can be used to issue sampling requests to the client.</returns>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    public IChatClient AsSamplingChatClient()
    {
        ThrowIfSamplingUnsupported();
        return new SamplingChatClient(this);
    }

    /// <summary>Gets an <see cref="ILogger"/> on which logged messages will be sent as notifications to the client.</summary>
    /// <returns>An <see cref="ILogger"/> that can be used to log to the client..</returns>
    public ILoggerProvider AsClientLoggerProvider()
    {
        return new ClientLoggerProvider(this);
    }

    /// <summary>
    /// Requests the client to list the roots it exposes.
    /// </summary>
    /// <param name="request">The parameters for the list roots request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the list of roots exposed by the client.</returns>
    /// <exception cref="InvalidOperationException">The client does not support roots.</exception>
    public ValueTask<ListRootsResult> RequestRootsAsync(
        ListRootsRequestParams request, CancellationToken cancellationToken = default)
    {
        ThrowIfRootsUnsupported();

        return SendRequestAsync(
            RequestMethods.RootsList,
            request,
            McpJsonUtilities.JsonContext.Default.ListRootsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListRootsResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests additional information from the user via the client, allowing the server to elicit structured data.
    /// </summary>
    /// <param name="request">The parameters for the elicitation request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the elicitation result.</returns>
    /// <exception cref="InvalidOperationException">The client does not support elicitation.</exception>
    public ValueTask<ElicitResult> ElicitAsync(
        ElicitRequestParams request, CancellationToken cancellationToken = default)
    {
        ThrowIfElicitationUnsupported();

        return SendRequestAsync(
            RequestMethods.ElicitationCreate,
            request,
            McpJsonUtilities.JsonContext.Default.ElicitRequestParams,
            McpJsonUtilities.JsonContext.Default.ElicitResult,
            cancellationToken: cancellationToken);
    }

    private void ThrowIfSamplingUnsupported()
    {
        if (ClientCapabilities?.Sampling is null)
        {
            if (ServerOptions.KnownClientInfo is not null)
            {
                throw new InvalidOperationException("Sampling is not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support sampling.");
        }
    }

    private void ThrowIfRootsUnsupported()
    {
        if (ClientCapabilities?.Roots is null)
        {
            if (ServerOptions.KnownClientInfo is not null)
            {
                throw new InvalidOperationException("Roots are not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support roots.");
        }
    }

    private void ThrowIfElicitationUnsupported()
    {
        if (ClientCapabilities?.Elicitation is null)
        {
            if (ServerOptions.KnownClientInfo is not null)
            {
                throw new InvalidOperationException("Elicitation is not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support elicitation requests.");
        }
    }

    /// <summary>Provides an <see cref="IChatClient"/> implementation that's implemented via client sampling.</summary>
    private sealed class SamplingChatClient : IChatClient
    {
        private readonly McpServer _server;

        public SamplingChatClient(McpServer server) => _server = server;

        /// <inheritdoc/>
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            _server.SampleAsync(messages, options, cancellationToken);

        /// <inheritdoc/>
        async IAsyncEnumerable<ChatResponseUpdate> IChatClient.GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        /// <inheritdoc/>
        object? IChatClient.GetService(Type serviceType, object? serviceKey)
        {
            Throw.IfNull(serviceType);

            return
                serviceKey is not null ? null :
                serviceType.IsInstanceOfType(this) ? this :
                serviceType.IsInstanceOfType(_server) ? _server :
                null;
        }

        /// <inheritdoc/>
        void IDisposable.Dispose() { } // nop
    }

    /// <summary>
    /// Provides an <see cref="ILoggerProvider"/> implementation for creating loggers
    /// that send logging message notifications to the client for logged messages.
    /// </summary>
    private sealed class ClientLoggerProvider : ILoggerProvider
    {
        private readonly McpServer _server;

        public ClientLoggerProvider(McpServer server) => _server = server;

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            Throw.IfNull(categoryName);

            return new ClientLogger(_server, categoryName);
        }

        /// <inheritdoc />
        void IDisposable.Dispose() { }

        private sealed class ClientLogger : ILogger
        {
            private readonly McpServer _server;
            private readonly string _categoryName;

            public ClientLogger(McpServer server, string categoryName)
            {
                _server = server;
                _categoryName = categoryName;
            }

            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) =>
                _server?.LoggingLevel is { } loggingLevel &&
                McpServerImpl.ToLoggingLevel(logLevel) >= loggingLevel;

            /// <inheritdoc />
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                Throw.IfNull(formatter);

                LogInternal(logLevel, formatter(state, exception));

                void LogInternal(LogLevel level, string message)
                {
                    _ = _server.SendNotificationAsync(NotificationMethods.LoggingMessageNotification, new LoggingMessageNotificationParams
                    {
                        Level = McpServerImpl.ToLoggingLevel(level),
                        Data = JsonSerializer.SerializeToElement(message, McpJsonUtilities.JsonContext.Default.String),
                        Logger = _categoryName,
                    });
                }
            }
        }
    }
}
