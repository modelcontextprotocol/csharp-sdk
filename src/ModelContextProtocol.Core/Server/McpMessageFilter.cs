namespace ModelContextProtocol.Server;

/// <summary>
/// Delegate type for applying filters to JSON-RPC messages.
/// </summary>
/// <param name="next">The next message handler in the pipeline.</param>
/// <returns>The next message handler wrapped with the filter.</returns>
/// <remarks>
/// <para>
/// Message filters allow you to intercept and process JSON-RPC messages before they reach
/// their respective handlers (incoming) or before they are sent (outgoing). This is useful for implementing
/// cross-cutting concerns that need to apply to all message types, such as logging, authentication, rate limiting,
/// redaction, or request tracing.
/// </para>
/// <para>
/// Filters are applied in the order they are registered, with the first registered filter being the outermost.
/// Each filter receives the next handler in the pipeline and can choose to:
/// <list type="bullet">
/// <item><description>Call the next handler to continue processing (await next(context, cancellationToken))</description></item>
/// <item><description>Skip the default handlers entirely by not calling next</description></item>
/// <item><description>Perform operations before and/or after calling next</description></item>
/// <item><description>Catch and handle exceptions from inner handlers</description></item>
/// </list>
/// </para>
/// <para>
/// For request-specific filters, use <see cref="McpRequestFilter{TParams, TResult}"/> instead.
/// </para>
/// </remarks>
public delegate McpMessageHandler McpMessageFilter(
    McpMessageHandler next);
