using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    /// <remarks>
    /// This method requires the client to support sampling capabilities.
    /// It allows detailed control over sampling parameters including messages, system prompt, temperature, 
    /// and token limits.
    /// </remarks>
    [Obsolete($"Use {nameof(McpServer)}.{nameof(McpServer.SampleAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<CreateMessageResult> SampleAsync(
        this IMcpServer server, CreateMessageRequestParams request, CancellationToken cancellationToken = default)
        => AsServerOrThrow(server).SampleAsync(request, cancellationToken);

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
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    /// <remarks>
    /// This method converts the provided chat messages into a format suitable for the sampling API,
    /// handling different content types such as text, images, and audio.
    /// </remarks>
    [Obsolete($"Use {nameof(McpServer)}.{nameof(McpServer.SampleAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774]
    public static Task<ChatResponse> SampleAsync(
        this IMcpServer server,
        IEnumerable<ChatMessage> messages, ChatOptions? options = default, CancellationToken cancellationToken = default)
        => AsServerOrThrow(server).SampleAsync(messages, options, cancellationToken);

    /// <summary>
    /// Creates an <see cref="IChatClient"/> wrapper that can be used to send sampling requests to the client.
    /// </summary>
    /// <param name="server">The server to be wrapped as an <see cref="IChatClient"/>.</param>
    /// <returns>The <see cref="IChatClient"/> that can be used to issue sampling requests to the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    [Obsolete($"Use {nameof(McpServer)}.{nameof(McpServer.AsSamplingChatClient)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static IChatClient AsSamplingChatClient(this IMcpServer server)
        => AsServerOrThrow(server).AsSamplingChatClient();

    /// <summary>Gets an <see cref="ILogger"/> on which logged messages will be sent as notifications to the client.</summary>
    /// <param name="server">The server to wrap as an <see cref="ILogger"/>.</param>
    /// <returns>An <see cref="ILogger"/> that can be used to log to the client..</returns>
    [Obsolete($"Use {nameof(McpServer)}.{nameof(McpServer.AsSamplingChatClient)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ILoggerProvider AsClientLoggerProvider(this IMcpServer server)
        => AsServerOrThrow(server).AsClientLoggerProvider();

    /// <summary>
    /// Requests the client to list the roots it exposes.
    /// </summary>
    /// <param name="server">The server initiating the request.</param>
    /// <param name="request">The parameters for the list roots request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the list of roots exposed by the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support roots.</exception>
    /// <remarks>
    /// This method requires the client to support the roots capability.
    /// Root resources allow clients to expose a hierarchical structure of resources that can be
    /// navigated and accessed by the server. These resources might include file systems, databases,
    /// or other structured data sources that the client makes available through the protocol.
    /// </remarks>
    [Obsolete($"Use {nameof(McpServer)}.{nameof(McpServer.RequestRootsAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<ListRootsResult> RequestRootsAsync(
        this IMcpServer server, ListRootsRequestParams request, CancellationToken cancellationToken = default)
        => AsServerOrThrow(server).RequestRootsAsync(request, cancellationToken);

    /// <summary>
    /// Requests additional information from the user via the client, allowing the server to elicit structured data.
    /// </summary>
    /// <param name="server">The server initiating the request.</param>
    /// <param name="request">The parameters for the elicitation request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the elicitation result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support elicitation.</exception>
    /// <remarks>
    /// This method requires the client to support the elicitation capability.
    /// </remarks>
    [Obsolete($"Use {nameof(McpServer)}.{nameof(McpServer.ElicitAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<ElicitResult> ElicitAsync(
        this IMcpServer server, ElicitRequestParams request, CancellationToken cancellationToken = default)
        => AsServerOrThrow(server).ElicitAsync(request, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable CS0618 // Type or member is obsolete
    private static McpServer AsServerOrThrow(IMcpServer server, [CallerMemberName] string memberName = "")
#pragma warning restore CS0618 // Type or member is obsolete
    {
        if (server is not McpServer mcpServer)
        {
            ThrowInvalidSessionType(memberName);
        }

        return mcpServer;

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowInvalidSessionType(string memberName)
            => throw new InvalidOperationException(
                $"Only arguments assignable to '{nameof(McpServer)}' are supported. " +
                $"Prefer using '{nameof(McpServer)}.{memberName}' instead, as " +
                $"'{nameof(McpServerExtensions)}.{memberName}' is obsolete and will be " +
                $"removed in the future.");
    }
}
