using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides extension methods for interacting with an <see cref="McpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class contains extension methods that simplify common operations with an MCP client,
/// such as pinging a server, listing and working with tools, prompts, and resources, and
/// managing subscriptions to resources.
/// </para>
/// </remarks>
public static class McpClientExtensions
{
    /// <summary>
    /// Creates a sampling handler for use with <see cref="McpClientHandlers.SamplingHandler"/> that will
    /// satisfy sampling requests using the specified <see cref="IChatClient"/>.
    /// </summary>
    /// <param name="chatClient">The <see cref="IChatClient"/> with which to satisfy sampling requests.</param>
    /// <returns>The created handler delegate that can be assigned to <see cref="McpClientHandlers.SamplingHandler"/>.</returns>
    /// <remarks>
    /// <para>
    /// This method creates a function that converts MCP message requests into chat client calls, enabling
    /// an MCP client to generate text or other content using an actual AI model via the provided chat client.
    /// </para>
    /// <para>
    /// The handler can process text messages, image messages, and resource messages as defined in the
    /// Model Context Protocol.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    public static Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, ValueTask<CreateMessageResult>> CreateSamplingHandler(
        this IChatClient chatClient)
    {
        Throw.IfNull(chatClient);

        return async (requestParams, progress, cancellationToken) =>
        {
            Throw.IfNull(requestParams);

            var (messages, options) = requestParams.ToChatClientArguments();
            var progressToken = requestParams.ProgressToken;

            List<ChatResponseUpdate> updates = [];
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                updates.Add(update);

                if (progressToken is not null)
                {
                    progress.Report(new()
                    {
                        Progress = updates.Count,
                    });
                }
            }

            return updates.ToChatResponse().ToCreateMessageResult();
        };
    }

    /// <summary>
    /// Converts the contents of a <see cref="CreateMessageRequestParams"/> into a pair of
    /// <see cref="IEnumerable{ChatMessage}"/> and <see cref="ChatOptions"/> instances to use
    /// as inputs into a <see cref="IChatClient"/> operation.
    /// </summary>
    /// <param name="requestParams"></param>
    /// <returns>The created pair of messages and options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    internal static (IList<ChatMessage> Messages, ChatOptions? Options) ToChatClientArguments(
        this CreateMessageRequestParams requestParams)
    {
        Throw.IfNull(requestParams);

        ChatOptions? options = null;

        if (requestParams.MaxTokens is int maxTokens)
        {
            (options ??= new()).MaxOutputTokens = maxTokens;
        }

        if (requestParams.Temperature is float temperature)
        {
            (options ??= new()).Temperature = temperature;
        }

        if (requestParams.StopSequences is { } stopSequences)
        {
            (options ??= new()).StopSequences = stopSequences.ToArray();
        }

        List<ChatMessage> messages =
            (from sm in requestParams.Messages
             let aiContent = sm.Content.ToAIContent()
             where aiContent is not null
             select new ChatMessage(sm.Role == Role.Assistant ? ChatRole.Assistant : ChatRole.User, [aiContent]))
            .ToList();

        return (messages, options);
    }

    /// <summary>Converts the contents of a <see cref="ChatResponse"/> into a <see cref="CreateMessageResult"/>.</summary>
    /// <param name="chatResponse">The <see cref="ChatResponse"/> whose contents should be extracted.</param>
    /// <returns>The created <see cref="CreateMessageResult"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chatResponse"/> is <see langword="null"/>.</exception>
    internal static CreateMessageResult ToCreateMessageResult(this ChatResponse chatResponse)
    {
        Throw.IfNull(chatResponse);

        // The ChatResponse can include multiple messages, of varying modalities, but CreateMessageResult supports
        // only either a single blob of text or a single image. Heuristically, we'll use an image if there is one
        // in any of the response messages, or we'll use all the text from them concatenated, otherwise.

        ChatMessage? lastMessage = chatResponse.Messages.LastOrDefault();

        ContentBlock? content = null;
        if (lastMessage is not null)
        {
            foreach (var lmc in lastMessage.Contents)
            {
                if (lmc is DataContent dc && (dc.HasTopLevelMediaType("image") || dc.HasTopLevelMediaType("audio")))
                {
                    content = dc.ToContent();
                }
            }
        }

        return new()
        {
            Content = content ?? new TextContentBlock { Text = lastMessage?.Text ?? string.Empty },
            Model = chatResponse.ModelId ?? "unknown",
            Role = lastMessage?.Role == ChatRole.User ? Role.User : Role.Assistant,
            StopReason = chatResponse.FinishReason == ChatFinishReason.Length ? "maxTokens" : "endTurn",
        };
    }
}