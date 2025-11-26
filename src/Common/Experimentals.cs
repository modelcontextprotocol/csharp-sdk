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
/// Diagnostic IDs cannot be reused when experimental API are removed or promoted to stable.
/// This ensures that users do not suppress warnings for new diagnostics with existing
/// suppressions that might be left in place from prior uses of the same diagnostic ID.
/// </para>
/// </remarks>
internal static class Experimentals
{
    // public const string Tasks_DiagnosticId = "MCP50001";
    // public const string Tasks_Message = "The Tasks feature is experimental within specification version 2025-11-25 and is subject to change. See SEP-1686 for more information.";
    // public const string Tasks_Url = "https://github.com/modelcontextprotocol/modelcontextprotocol/issues/1686";
}
