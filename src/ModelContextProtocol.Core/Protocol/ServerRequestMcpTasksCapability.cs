using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents task support for server request types.
/// </summary>
/// <remarks>
/// Servers can only support task augmentation for <c>tools/call</c> requests.
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ServerRequestMcpTasksCapability
{
    /// <summary>
    /// Gets or sets task support for tool-related requests.
    /// </summary>
    [JsonPropertyName("tools")]
    public ToolsMcpTasksCapability? Tools { get; set; }
}

/// <summary>
/// Represents task support for tool-related requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ToolsMcpTasksCapability
{
    /// <summary>
    /// Gets or sets whether tools/call requests support task augmentation.
    /// </summary>
    [JsonPropertyName("call")]
    public CallToolMcpTasksCapability? Call { get; set; }
}

/// <summary>
/// Represents the capability for task-augmented tools/call requests.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CallToolMcpTasksCapability;
