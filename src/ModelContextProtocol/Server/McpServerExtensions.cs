using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;

namespace ModelContextProtocol.Server;

/// <inheritdoc />
public static class McpServerExtensions
{
    /// <summary>
    /// Sends a logging message notification to the client.
    /// </summary>
    /// <param name="server">The server instance that will handle the log notification request.</param>
    /// <param name="loggingMessageNotification">Contains the details of the log message to be sent.</param>
    /// <param name="cancellationToken">Allows the operation to be canceled if needed.</param>
    /// <returns>Returns a task representing the asynchronous operation.</returns>
    public static Task SendLogNotificationAsync(this IMcpServer server, LoggingMessageNotificationParams loggingMessageNotification, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(server);
        Throw.IfNull(loggingMessageNotification);
        return server.SendRequestAsync<EmptyResult>(
            new JsonRpcRequest { Method = "notifications/message", Params = loggingMessageNotification },
            cancellationToken);
    }

    /// <summary>
    /// Requests to sample an LLM via the client.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The client does not support sampling.</exception>
    public static Task<CreateMessageResult> RequestSamplingAsync(
        this IMcpServer server, CreateMessageRequestParams request, CancellationToken cancellationToken)
    {
        Throw.IfNull(server);

        if (server.ClientCapabilities?.Sampling is null)
        {
            throw new ArgumentException("Client connected to the server does not support sampling.", nameof(server));
        }

        return server.SendRequestAsync<CreateMessageResult>(
            new JsonRpcRequest { Method = "sampling/createMessage", Params = request },
            cancellationToken);
    }

    /// <summary>
    /// Requests to sample an LLM via the client.
    /// </summary>
    /// <param name="server">The server issueing the request.</param>
    /// <param name="messages">The messages to send as part of the request.</param>
    /// <param name="options">The options to use for the request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing the response from the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The client does not support sampling.</exception>
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

                        case DataContent dataContent when dataContent.HasTopLevelMediaType("image"):
                            samplingMessages.Add(new()
                            {
                                Role = role,
                                Content = new()
                                {
                                    Type = "image",
                                    MimeType = dataContent.MediaType,
                                    Data = Convert.ToBase64String(dataContent.Data.
#if NET
                                        Span),
#else
                                        ToArray()),
#endif
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

        ChatMessage responseMessage = new()
        {
            Role = result.Role == "user" ? ChatRole.User : ChatRole.Assistant
        };

        if (result.Content is { Type: "text" })
        {
            responseMessage.Contents.Add(new TextContent(result.Content.Text));
        }
        else if (result.Content is { Type: "image", MimeType: not null, Data: not null })
        {
            responseMessage.Contents.Add(new DataContent(Convert.FromBase64String(result.Content.Data), result.Content.MimeType));
        }
        else if (result.Content is { Type: "resource" } && result.Content.Resource is { } resourceContents)
        {
            if (resourceContents.Text is not null)
            {
                responseMessage.Contents.Add(new TextContent(resourceContents.Text));
            }

            if (resourceContents.Blob is not null && resourceContents.MimeType is not null)
            {
                responseMessage.Contents.Add(new DataContent(Convert.FromBase64String(resourceContents.Blob), resourceContents.MimeType));
            }
        }

        return new(responseMessage)
        {
            ModelId = result.Model,
            FinishReason = result.StopReason switch
            {
                "maxTokens" => ChatFinishReason.Length,
                "endTurn" or "stopSequence" or _ => ChatFinishReason.Stop,
            }
        };
    }

    /// <summary>Creates an <see cref="IChatClient"/> that can be used to send sampling requests to the client.</summary>
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

    /// <summary>
    /// Requests the client to list the roots it exposes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The client does not support roots.</exception>
    public static Task<ListRootsResult> RequestRootsAsync(
        this IMcpServer server, ListRootsRequestParams request, CancellationToken cancellationToken)
    {
        Throw.IfNull(server);

        if (server.ClientCapabilities?.Roots is null)
        {
            throw new ArgumentException("Client connected to the server does not support roots.", nameof(server));
        }

        return server.SendRequestAsync<ListRootsResult>(
            new JsonRpcRequest { Method = "roots/list", Params = request },
            cancellationToken);
    }

    /// <summary>Provides an <see cref="IChatClient"/> implementation that's implemented via client sampling.</summary>
    /// <param name="server"></param>
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
}
