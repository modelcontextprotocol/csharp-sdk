namespace ModelContextProtocol.Server;

/// <summary>
/// Delegate type for handling incoming JSON-RPC messages.
/// </summary>
/// <param name="context">The message context containing the JSON-RPC message and other metadata.</param>
/// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
/// <returns>A task representing the asynchronous operation.</returns>
/// <remarks>
/// <para>
/// This delegate can handle any type of JSON-RPC message, including requests, notifications, responses, and errors.
/// Use this for implementing cross-cutting concerns that need to intercept all message types,
/// such as logging, authentication, rate limiting, or request tracing.
/// </para>
/// <para>
/// For request-specific handling, use <see cref="McpRequestHandler{TParams, TResult}"/> instead.
/// </para>
/// </remarks>
public delegate Task McpMessageHandler(
    MessageContext context,
    CancellationToken cancellationToken);
