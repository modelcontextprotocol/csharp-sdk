using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol;

/// <summary>
/// Defines diagnostic IDs, Messages, and Urls for APIs annotated with <see cref="ExperimentalAttribute"/>.
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
    public const string Extensions_DiagnosticId = "MCPEXP001";

    /// <summary>
    /// Message for the experimental MCP Extensions feature.
    /// </summary>
    public const string Extensions_Message = "The Extensions feature is experimental per the MCP specification and is subject to change.";

    /// <summary>
    /// URL for the experimental MCP Extensions feature.
    /// </summary>
    public const string Extensions_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#mcpexp001";
}
