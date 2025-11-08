using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModelContextProtocol;

/// <summary>
/// Provides extension methods for interacting with an <see cref="IMcpEndpoint"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class provides strongly-typed methods for working with the Model Context Protocol (MCP) endpoints,
/// simplifying JSON-RPC communication by handling serialization and deserialization of parameters and results.
/// </para>
/// <para>
/// These extension methods are designed to be used with both client (<see cref="IMcpClient"/>) and
/// server (<see cref="IMcpServer"/>) implementations of the <see cref="IMcpEndpoint"/> interface.
/// </para>
/// </remarks>
public static class McpEndpointExtensions
{
    /// <summary>
    /// Sends a JSON-RPC request and attempts to deserialize the result to <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TParameters">The type of the request parameters to serialize from.</typeparam>
    /// <typeparam name="TResult">The type of the result to deserialize to.</typeparam>
    /// <param name="endpoint">The MCP client or server instance.</param>
    /// <param name="method">The JSON-RPC method name to invoke.</param>
    /// <param name="parameters">Object representing the request parameters.</param>
    /// <param name="requestId">The request id for the request.</param>
    /// <param name="serializerOptions">The options governing request serialization.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized result.</returns>
    [Obsolete($"Use {nameof(McpSession)}.{nameof(McpSession.SendRequestAsync)} instead. This member will be removed in a subsequent release.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ValueTask<TResult> SendRequestAsync<TParameters, TResult>(
        this IMcpEndpoint endpoint,
        string method,
        TParameters parameters,
        JsonSerializerOptions? serializerOptions = null,
        RequestId requestId = default,
        CancellationToken cancellationToken = default)
        where TResult : notnull
        => AsSessionOrThrow(endpoint).SendRequestAsync<TParameters, TResult>(method, parameters, serializerOptions, requestId, cancellationToken);

    /// <summary>
    /// Sends a parameterless notification to the connected endpoint.
    /// </summary>
    /// <param name="client">The MCP client or server instance.</param>
    /// <param name="method">The notification method name.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// <para>
    /// This method sends a notification without any parameters. Notifications are one-way messages
    /// that don't expect a response. They are commonly used for events, status updates, or to signal
    /// changes in state.
    /// </para>
    /// </remarks>
    [Obsolete($"Use {nameof(McpSession)}.{nameof(McpSession.SendNotificationAsync)} instead. This member will be removed in a subsequent release.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task SendNotificationAsync(this IMcpEndpoint client, string method, CancellationToken cancellationToken = default)
        => AsSessionOrThrow(client).SendNotificationAsync(method, cancellationToken);

    /// <summary>
    /// Sends a notification with parameters to the connected endpoint.
    /// </summary>
    /// <typeparam name="TParameters">The type of the notification parameters to serialize.</typeparam>
    /// <param name="endpoint">The MCP client or server instance.</param>
    /// <param name="method">The JSON-RPC method name for the notification.</param>
    /// <param name="parameters">Object representing the notification parameters.</param>
    /// <param name="serializerOptions">The options governing parameter serialization. If null, default options are used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// <para>
    /// This method sends a notification with parameters to the connected endpoint. Notifications are one-way
    /// messages that don't expect a response, commonly used for events, status updates, or signaling changes.
    /// </para>
    /// <para>
    /// The parameters object is serialized to JSON according to the provided serializer options or the default
    /// options if none are specified.
    /// </para>
    /// <para>
    /// The Model Context Protocol defines several standard notification methods in <see cref="NotificationMethods"/>,
    /// but custom methods can also be used for application-specific notifications.
    /// </para>
    /// </remarks>
    [Obsolete($"Use {nameof(McpSession)}.{nameof(McpSession.SendNotificationAsync)} instead. This member will be removed in a subsequent release.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task SendNotificationAsync<TParameters>(
        this IMcpEndpoint endpoint,
        string method,
        TParameters parameters,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        => AsSessionOrThrow(endpoint).SendNotificationAsync(method, parameters, serializerOptions, cancellationToken);

    /// <summary>
    /// Notifies the connected endpoint of progress for a long-running operation.
    /// </summary>
    /// <param name="endpoint">The endpoint issuing the notification.</param>
    /// <param name="progressToken">The <see cref="ProgressToken"/> identifying the operation for which progress is being reported.</param>
    /// <param name="progress">The progress update to send, containing information such as percentage complete or status message.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the completion of the notification operation (not the operation being tracked).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method sends a progress notification to the connected endpoint using the Model Context Protocol's
    /// standardized progress notification format. Progress updates are identified by a <see cref="ProgressToken"/>
    /// that allows the recipient to correlate multiple updates with a specific long-running operation.
    /// </para>
    /// <para>
    /// Progress notifications are sent asynchronously and don't block the operation from continuing.
    /// </para>
    /// </remarks>
    [Obsolete($"Use {nameof(McpSession)}.{nameof(McpSession.NotifyProgressAsync)} instead. This member will be removed in a subsequent release.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task NotifyProgressAsync(
        this IMcpEndpoint endpoint,
        ProgressToken progressToken,
        ProgressNotificationValue progress,
        CancellationToken cancellationToken = default)
        => AsSessionOrThrow(endpoint).NotifyProgressAsync(progressToken, progress, null, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable CS0618 // Type or member is obsolete
    private static McpSession AsSessionOrThrow(IMcpEndpoint endpoint, [CallerMemberName] string memberName = "")
#pragma warning restore CS0618 // Type or member is obsolete
    {
        if (endpoint is not McpSession session)
        {
            ThrowInvalidEndpointType(memberName);
        }

        return session;

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowInvalidEndpointType(string memberName)
            => throw new InvalidOperationException(
                $"Only arguments assignable to '{nameof(McpSession)}' are supported. " +
                $"Prefer using '{nameof(McpServer)}.{memberName}' instead, as " +
                $"'{nameof(McpEndpointExtensions)}.{memberName}' is obsolete and will be " +
                $"removed in the future.");
    }
}
