using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides filter collections for outgoing MCP client requests.
/// </summary>
/// <remarks>
/// Filters allow middleware-style composition where a filter can perform actions before and after the inner handler,
/// mirroring <see cref="Server.McpServerFilters"/> on the server.
/// </remarks>
[Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
public sealed class McpClientFilters
{
    /// <summary>
    /// Gets or sets the filters for request-specific client pipelines.
    /// </summary>
    public McpClientRequestFilters Request
    {
        get => field ??= new();
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }
}
