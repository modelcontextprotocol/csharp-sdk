using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents an instance of a Model Context Protocol (MCP) client session that connects to and communicates with an MCP server.
/// </summary>
public abstract partial class McpClient : McpSession
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpClient"/> class.
    /// </summary>
    [Experimental(Experimentals.Subclassing_DiagnosticId, UrlFormat = Experimentals.Subclassing_Url)]
    protected McpClient()
    {
    }

    /// <summary>
    /// Gets the capabilities supported by the connected server.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client is not connected.</exception>
    public abstract ServerCapabilities ServerCapabilities { get; }

    /// <summary>
    /// Gets the implementation information of the connected server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property provides identification details about the connected server, including its name and version.
    /// It is populated during the initialization handshake and is available after a successful connection.
    /// </para>
    /// <para>
    /// This information can be useful for logging, debugging, compatibility checks, and displaying server
    /// information to users.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The client is not connected.</exception>
    public abstract Implementation ServerInfo { get; }

    /// <summary>
    /// Gets any instructions describing how to use the connected server and its features.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property contains instructions provided by the server during initialization that explain
    /// how to effectively use its capabilities. They should focus on guidance that helps a model
    /// use the server effectively and should avoid duplicating tool, prompt, or resource descriptions.
    /// </para>
    /// <para>
    /// This can be used by clients to improve an LLM's understanding of how to use the server.
    /// It can be thought of like a "hint" to the model and can be added to a system prompt.
    /// </para>
    /// </remarks>
    public abstract string? ServerInstructions { get; }

    /// <summary>
    /// Gets a <see cref="Task{TResult}"/> that completes when the client session has completed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The task always completes successfully. The result provides details about why the session
    /// completed. Transport implementations may return derived types with additional strongly-typed
    /// information, such as <see cref="StdioClientCompletionDetails"/>.
    /// </para>
    /// <para>
    /// For graceful closure (e.g., explicit disposal), <see cref="ClientCompletionDetails.Exception"/>
    /// will be <see langword="null"/>. For unexpected closure (e.g., process crash, network failure),
    /// it may contain an exception that caused or that represents the failure.
    /// </para>
    /// </remarks>
    public abstract Task<ClientCompletionDetails> Completion { get; }

    /// <summary>
    /// Resolves input requests embedded in an <see cref="InputRequiredTaskResult"/> by dispatching
    /// each request to the appropriate registered handler.
    /// </summary>
    /// <param name="inputRequests">
    /// The input requests from the task, keyed by request identifier. Each value is an
    /// <see cref="InputRequest"/> wrapping the server-to-client request payload.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A dictionary of responses keyed by the same identifiers as the input requests.</returns>
    private protected abstract ValueTask<IDictionary<string, InputResponse>> ResolveInputRequestsAsync(
        IDictionary<string, InputRequest> inputRequests, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the maximum number of consecutive stuck-in-<see cref="McpTaskStatus.InputRequired"/> polls
    /// allowed by <see cref="PollTaskToCompletionAsync"/> before the client cancels and throws.
    /// Sourced from <see cref="McpClientOptions.MaxConsecutiveStuckPolls"/>.
    /// </summary>
    private protected abstract int MaxConsecutiveStuckPolls { get; }

    /// <summary>
    /// Inspects a received cacheable result (<c>tools/list</c>, <c>prompts/list</c>, <c>resources/list</c>,
    /// <c>resources/templates/list</c>, or <c>resources/read</c>) so derived clients can emit diagnostics.
    /// </summary>
    /// <param name="method">The request method that produced the result.</param>
    /// <param name="result">The cacheable result returned by the server.</param>
    /// <remarks>
    /// This is used to warn (never throw) when a server that negotiated a protocol version requiring the
    /// SEP-2549 <c>ttlMs</c>/<c>cacheScope</c> fields omits them. The default implementation does nothing.
    /// </remarks>
    private protected virtual void ValidateCacheableResult(string method, ICacheableResult result)
    {
    }

    /// <summary>
    /// Registers one or more tool definitions in the client's tool cache, enabling the transport
    /// to send <c>Mcp-Param-*</c> headers for those tools without requiring a prior <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/> call.
    /// </summary>
    /// <param name="tools">The tool definitions to register.</param>
    /// <remarks>
    /// <para>
    /// This method allows callers who already have tool schema information (e.g., from a previous session,
    /// hardcoded configuration, or an out-of-band source) to provide it directly to the client. Once registered,
    /// any <see cref="McpClient.CallToolAsync(string, IReadOnlyDictionary{string, object?}?, IProgress{ProgressNotificationValue}?, RequestOptions?, CancellationToken)"/>
    /// call for a registered tool will automatically include <c>Mcp-Param-*</c> HTTP headers based on
    /// the tool's <c>x-mcp-header</c> schema annotations, exactly as if the tool had been discovered
    /// via <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// <b>Cache interaction behavior:</b>
    /// <list type="bullet">
    ///   <item>Registered tools are added to the same internal tool cache used by <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/>.</item>
    ///   <item>Calling <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/> after <see cref="AddKnownTools"/> preserves
    ///     manually registered tools — only server-discovered tools are cleared and repopulated.</item>
    ///   <item>If the server returns a tool with the same name as a manually registered tool, the server's
    ///     definition overwrites the registered one in the cache, but the tool retains its known status
    ///     and will survive subsequent cache clears. This registration is sticky for the lifetime of the
    ///     <see cref="McpClient"/>; use <see cref="RemoveKnownTools"/> or <see cref="ClearKnownTools"/> to
    ///     explicitly drop known tools that are no longer needed.</item>
    ///   <item>Tools can be registered at any time — before or after <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/>,
    ///     and across multiple calls.</item>
    ///   <item>Re-registering a tool with the same name overwrites the previous definition in the cache (last write wins).</item>
    /// </list>
    /// </para>
    /// <para>
    /// Tools with invalid <c>x-mcp-header</c> annotations cause an <see cref="ArgumentException"/> to be thrown.
    /// No tools are added to the cache if any tool in the batch fails validation (all-or-nothing).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tools"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One or more tools have invalid <c>x-mcp-header</c> annotations.</exception>
    public virtual void AddKnownTools(IEnumerable<Tool> tools)
    {
        Throw.IfNull(tools);
        throw new NotSupportedException($"{GetType().Name} does not support adding known tools.");
    }

    /// <summary>
    /// Removes one or more previously registered tool definitions from the client's tool cache by name.
    /// </summary>
    /// <param name="toolNames">The names of the tools to remove.</param>
    /// <remarks>
    /// <para>
    /// This removes the specified tools from both the known-tools set and the internal tool cache.
    /// After removal, those tools will no longer survive <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/>
    /// cache clears, and <c>Mcp-Param-*</c> headers will no longer be sent for them unless the server
    /// re-discovers them via <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// Removing a tool name that was not previously added via <see cref="AddKnownTools"/> is a no-op.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="toolNames"/> is <see langword="null"/>.</exception>
    public virtual void RemoveKnownTools(IEnumerable<string> toolNames)
    {
        Throw.IfNull(toolNames);
        throw new NotSupportedException($"{GetType().Name} does not support removing known tools.");
    }

    /// <summary>
    /// Removes all previously registered tool definitions from the client's tool cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This clears all tools that were added via <see cref="AddKnownTools"/> from both the known-tools
    /// set and the internal tool cache. Server-discovered tools that are not also known tools are not affected
    /// and will remain in the cache until the next <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/> call.
    /// </para>
    /// </remarks>
    public virtual void ClearKnownTools()
    {
        throw new NotSupportedException($"{GetType().Name} does not support clearing known tools.");
    }
}
