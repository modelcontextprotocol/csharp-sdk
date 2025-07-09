using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Core;

/// TODO:
public interface IToolFilter
{
    /// TODO:
    public int Order { get; }
    
    /// TODO:
    bool OnToolListed(Tool tool, RequestContext<ListToolsRequestParams> context);

    /// TODO:
    ValueTask<CallToolResult>? OnToolCalling(Tool tool, RequestContext<CallToolRequestParams> context);
    
    /// TODO:
    ValueTask<CallToolResult>? OnToolCalled(Tool tool, RequestContext<CallToolRequestParams> context, ValueTask<CallToolResult> callResult);
}

/// TODO:
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public abstract class ToolFilter(int order) : Attribute, IToolFilter
{
    /// <inheritdoc />
    public int Order { get; } = order;

    /// <inheritdoc />
    public virtual bool OnToolListed(Tool tool, RequestContext<ListToolsRequestParams> context) => true;

    /// <inheritdoc />
    public virtual ValueTask<CallToolResult>? OnToolCalling(Tool tool, RequestContext<CallToolRequestParams> context) =>
        null;

    /// <inheritdoc />
    public virtual ValueTask<CallToolResult>? OnToolCalled(Tool tool, RequestContext<CallToolRequestParams> context,
        ValueTask<CallToolResult> callResult) => null;
}