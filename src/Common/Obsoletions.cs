namespace ModelContextProtocol;

/// <summary>
/// Defines diagnostic IDs, Messages, and Urls for APIs annotated with <see cref="ObsoleteAttribute"/>.
/// </summary>
/// <remarks>
/// When a deprecated API is associated with an specification change, the message
/// should refer to the specification version that introduces the change and the SEP
/// when available. If there is a SEP associated with the experimental API, the Url should
/// point to the SEP issue.
/// <para>
/// Obsolete diagnostic IDs are in the format MCP9###.
/// </para>
/// <para>
/// Diagnostic IDs cannot be reused when obsolete APIs are removed or restored.
/// This ensures that users do not suppress warnings for new diagnostics with existing
/// suppressions that might be left in place from prior uses of the same diagnostic ID.
/// </para>
/// </remarks>
internal static class Obsoletions
{
    public const string LegacyTitledEnumSchema_DiagnosticId = "MCP9001";
    public const string LegacyTitledEnumSchema_Message = "The EnumSchema and LegacyTitledEnumSchema APIs are deprecated as of specification version 2025-11-25 and will be removed in a future major version. See SEP-1330 for more information.";
    public const string LegacyTitledEnumSchema_Url = "https://github.com/modelcontextprotocol/modelcontextprotocol/issues/1330";

    public const string MessageAndRequestFilter_DiagnosticId = "MCP9002";
    public const string MessageAndRequestFilter_Url = "https://github.com/modelcontextprotocol/csharp-sdk/pull/1308";
    public const string MessageFilter_Message = "Use WithMessageFilters() instead.";
    public const string RequestFilter_Message = "Use WithRequestFilters() instead.";
}
