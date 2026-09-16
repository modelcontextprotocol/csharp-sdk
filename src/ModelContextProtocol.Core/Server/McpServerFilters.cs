using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides filter collections for MCP server handlers.
/// </summary>
/// <remarks>
/// This class contains collections of filters that can be applied to various MCP server handlers.
/// This allows for middleware-style composition where filters can perform actions before and after the inner handler.
/// </remarks>
public sealed class McpServerFilters
{
    /// <summary>
    /// Gets or sets the filters for incoming and outgoing JSON-RPC messages.
    /// </summary>
    public McpMessageFilters Message
    {
        get => field ??= new();
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for request-specific MCP handler pipelines.
    /// </summary>
    public McpRequestFilters Request
    {
        get => field ??= new();
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }
}
