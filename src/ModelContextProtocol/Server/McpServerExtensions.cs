using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides extension methods for interacting with an <see cref="IMcpServer"/> instance.
/// </summary>
public static class McpServerExtensions
{
    /// <summary>
    /// Requests to sample an LLM via the client using the specified request parameters.
    /// </summary>
    /// <param name="server">The server instance initiating the request.</param>
    /// <param name="request">The parameters for the sampling request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the sampling result from the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The client does not support sampling.</exception>
    /// <remarks>
    /// This method requires the client to support sampling capabilities.
    /// It allows detailed control over sampling parameters including messages, system prompt, temperature, 
    /// and token limits.
    /// </remarks>
    public static Task<CreateMessageResult> RequestSamplingAsync(
        this IMcpServer server, CreateMessageRequestParams request, CancellationToken cancellationToken)
    {
        Throw.IfNull(server);

        if (server.ClientCapabilities?.Sampling is null)
        {
            throw new ArgumentException("Client connected to the server does not support sampling.", nameof(server));
        }

        return server.SendRequestAsync(
            RequestMethods.SamplingCreateMessage,
            request,
            McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
            McpJsonUtilities.JsonContext.Default.CreateMessageResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests to sample an LLM via the client using the provided chat messages and options.
    /// </summary>
    /// <param name="server">The server initiating the request.</param>
    /// <param name="messages">The messages to send as part of the request.</param>
    /// <param name="options">The options to use for the request, including model parameters and constraints.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the chat response from the model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The client does not support sampling.</exception>
    /// <remarks>
    /// This method converts the provided chat messages into a format suitable for the sampling API,
    /// handling different content types such as text, images, and audio.
    /// </remarks>
    public static async Task<ChatResponse> RequestSamplingAsync(
        this IMcpServer server,
        IEnumerable<ChatMessage> messages, ChatOptions? options = default, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(server);
        Throw.IfNull(messages);

        StringBuilder? systemPrompt = null;

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
                                Content = new()
                                {
                                    Type = "text",
                                    Text = textContent.Text,
                                },
                            });
                            break;

                        case DataContent dataContent when dataContent.HasTopLevelMediaType("image") || dataContent.HasTopLevelMediaType("audio"):
                            samplingMessages.Add(new()
                            {
                                Role = role,
                                Content = new()
                                {
                                    Type = dataContent.HasTopLevelMediaType("image") ? "image" : "audio",
                                    MimeType = dataContent.MediaType,
                                    Data = dataContent.GetBase64Data(),
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

        var result = await server.RequestSamplingAsync(new()
            {
                Messages = samplingMessages,
                MaxTokens = options?.MaxOutputTokens,
                StopSequences = options?.StopSequences?.ToArray(),
                SystemPrompt = systemPrompt?.ToString(),
                Temperature = options?.Temperature,
                ModelPreferences = modelPreferences,
            }, cancellationToken).ConfigureAwait(false);

        return new(new ChatMessage(new(result.Role), [result.Content.ToAIContent()]))
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
    /// <param name="server">The server to be wrapped as an <see cref="IChatClient"/>.</param>
    /// <returns>The <see cref="IChatClient"/> that can be used to issue sampling requests to the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The client does not support sampling.</exception>
    public static IChatClient AsSamplingChatClient(this IMcpServer server)
    {
        Throw.IfNull(server);

        if (server.ClientCapabilities?.Sampling is null)
        {
            throw new ArgumentException("Client connected to the server does not support sampling.", nameof(server));
        }

        return new SamplingChatClient(server);
    }

    /// <summary>Gets an <see cref="ILogger"/> on which logged messages will be sent as notifications to the client.</summary>
    /// <param name="server">The server to wrap as an <see cref="ILogger"/>.</param>
    /// <returns>An <see cref="ILogger"/> that can be used to log to the client..</returns>
    public static ILoggerProvider AsClientLoggerProvider(this IMcpServer server)
    {
        Throw.IfNull(server);

        return new ClientLoggerProvider(server);
    }

    /// <summary>
    /// Requests the client to list the roots it exposes.
    /// </summary>
    /// <param name="server">The server initiating the request.</param>
    /// <param name="request">The parameters for the list roots request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the list of roots exposed by the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The client does not support roots.</exception>
    /// <remarks>
    /// This method requires the client to support the roots capability.
    /// Root resources allow clients to expose a hierarchical structure of resources that can be
    /// navigated and accessed by the server. These resources might include file systems, databases,
    /// or other structured data sources that the client makes available through the protocol.
    /// </remarks>
    public static Task<ListRootsResult> RequestRootsAsync(
        this IMcpServer server, ListRootsRequestParams request, CancellationToken cancellationToken)
    {
        Throw.IfNull(server);

        if (server.ClientCapabilities?.Roots is null)
        {
            throw new ArgumentException("Client connected to the server does not support roots.", nameof(server));
        }

        return server.SendRequestAsync(
            RequestMethods.RootsList,
            request,
            McpJsonUtilities.JsonContext.Default.ListRootsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListRootsResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>Provides an <see cref="IChatClient"/> implementation that's implemented via client sampling.</summary>
    private sealed class SamplingChatClient(IMcpServer server) : IChatClient
    {
        /// <inheritdoc/>
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            server.RequestSamplingAsync(messages, options, cancellationToken);

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
                serviceType.IsInstanceOfType(server) ? server :
                null;
        }

        /// <inheritdoc/>
        void IDisposable.Dispose() { } // nop
    }

    /// <summary>
    /// Provides an <see cref="ILoggerProvider"/> implementation for creating loggers
    /// that send logging message notifications to the client for logged messages.
    /// </summary>
    private sealed class ClientLoggerProvider(IMcpServer server) : ILoggerProvider
    {
        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            Throw.IfNull(categoryName);

            return new ClientLogger(server, categoryName);
        }

        /// <inheritdoc />
        void IDisposable.Dispose() { }

        private sealed class ClientLogger(IMcpServer server, string categoryName) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) =>
                server?.LoggingLevel is { } loggingLevel &&
                McpServer.ToLoggingLevel(logLevel) >= loggingLevel;

            /// <inheritdoc />
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                Throw.IfNull(formatter);

                Log(logLevel, formatter(state, exception));

                void Log(LogLevel logLevel, string message)
                {
                    _ = server.SendNotificationAsync(NotificationMethods.LoggingMessageNotification, new LoggingMessageNotificationParams()
                    {
                        Level = McpServer.ToLoggingLevel(logLevel),
                        Data = JsonSerializer.SerializeToElement(message, McpJsonUtilities.JsonContext.Default.String),
                        Logger = categoryName,
                    });
                }
            }
        }
    }
}
