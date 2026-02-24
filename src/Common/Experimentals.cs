using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol;

/// <summary>
/// Defines diagnostic IDs, messages, and URLs for APIs annotated with <see cref="ExperimentalAttribute"/>.
/// </summary>
/// <remarks>
/// When an experimental API is associated with an experimental specification, the message
/// should refer to the specification version that introduces the feature and the SEP
/// when available. If there is a SEP associated with the experimental API, the Url should
/// point to the SEP issue.
/// <para>
/// Experimental diagnostic IDs are in the format MCPEXP###.
/// </para>
/// <para>
/// Diagnostic IDs cannot be reused when experimental API are removed or promoted to stable.
/// This ensures that users do not suppress warnings for new diagnostics with existing
/// suppressions that might be left in place from prior uses of the same diagnostic ID.
/// </para>
/// </remarks>
internal static class Experimentals
{
    /// <summary>
    /// Diagnostic ID for the experimental MCP Tasks feature.
    /// </summary>
    public const string Tasks_DiagnosticId = "MCPEXP001";

    /// <summary>
    /// Message for the experimental MCP Tasks feature.
    /// </summary>
    public const string Tasks_Message = "The Tasks feature is experimental per the MCP specification and is subject to change.";

    /// <summary>
    /// URL for the experimental MCP Tasks feature.
    /// </summary>
    public const string Tasks_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#mcpexp001";

    /// <summary>
    /// Diagnostic ID for the experimental MCP Extensions feature.
    /// </summary>
    /// <remarks>
    /// This uses the same diagnostic ID as <see cref="Tasks_DiagnosticId"/> because both
    /// Tasks and Extensions are covered by the same MCPEXP001 diagnostic for experimental
    /// MCP features. Having separate constants improves code clarity while maintaining a
    /// single diagnostic suppression point.
    /// </remarks>
    public const string Extensions_DiagnosticId = "MCPEXP001";

    /// <summary>
    /// Message for the experimental MCP Extensions feature.
    /// </summary>
    public const string Extensions_Message = "The Extensions feature is part of a future MCP specification version that has not yet been ratified and is subject to change.";

    /// <summary>
    /// URL for the experimental MCP Extensions feature.
    /// </summary>
    public const string Extensions_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#mcpexp001";

    /// <summary>
    /// Diagnostic ID for experimental subclassing of McpClient and McpServer.
    /// </summary>
    public const string Subclassing_DiagnosticId = "MCPEXP002";

    /// <summary>
    /// Message for experimental subclassing of McpClient and McpServer.
    /// </summary>
    public const string Subclassing_Message = "Subclassing McpClient and McpServer is experimental and subject to change.";

    /// <summary>
    /// URL for experimental subclassing of McpClient and McpServer.
    /// </summary>
    public const string Subclassing_Url = "https://github.com/modelcontextprotocol/csharp-sdk/pull/1363";

    /// <summary>
    /// Diagnostic ID for the experimental <c>RunSessionHandler</c> API.
    /// </summary>
    public const string RunSessionHandler_DiagnosticId = "MCPEXP003";

    /// <summary>
    /// Message for the experimental <c>RunSessionHandler</c> API.
    /// </summary>
    public const string RunSessionHandler_Message = "RunSessionHandler is experimental and may be removed or changed in a future release. Consider using ConfigureSessionOptions instead.";

    /// <summary>
    /// URL for the experimental <c>RunSessionHandler</c> API.
    /// </summary>
    public const string RunSessionHandler_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#mcpexp003";
}
