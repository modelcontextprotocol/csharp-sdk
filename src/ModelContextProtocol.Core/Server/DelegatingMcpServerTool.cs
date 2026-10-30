using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="McpServerTool"/> that delegates all operations to an inner <see cref="McpServerTool"/>.</summary>
/// <remarks>
/// This is recommended as a base type when building tools that can be chained around an underlying <see cref="McpServerTool"/>.
/// The default implementation simply passes each call to the inner tool instance.
/// When an invocation override retains the original <see cref="CallToolResult.StructuredContent"/> element,
/// server dispatch can apply required-member recovery against the registered tool's effective output schema.
/// Overrides that replace or reserialize structured content are responsible for matching their advertised schema.
/// </remarks>
public abstract class DelegatingMcpServerTool : McpServerTool
{
    private readonly McpServerTool _innerTool;

    /// <summary>Initializes a new instance of the <see cref="DelegatingMcpServerTool"/> class around the specified <paramref name="innerTool"/>.</summary>
    /// <param name="innerTool">The inner tool wrapped by this delegating tool.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerTool"/> is <see langword="null"/>.</exception>
    protected DelegatingMcpServerTool(McpServerTool innerTool)
    {
        Throw.IfNull(innerTool);
        _innerTool = innerTool;
        HasCustomInvocation = ((Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>>)InvokeAsync)
            .Method.DeclaringType != typeof(DelegatingMcpServerTool);
    }

    /// <summary>Gets the tool wrapped by this delegating tool.</summary>
    internal McpServerTool InnerTool => _innerTool;

    internal bool HasCustomInvocation { get; }

    /// <inheritdoc />
    public override Tool ProtocolTool => _innerTool.ProtocolTool;

    /// <inheritdoc />
    public override IReadOnlyList<object> Metadata => _innerTool.Metadata;

    /// <inheritdoc />
    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default) =>
        _innerTool.InvokeAsync(request, cancellationToken);

    /// <inheritdoc />
    public override string ToString() => _innerTool.ToString();
}
