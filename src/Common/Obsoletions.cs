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
    // MCP9001 was used for LegacyTitledEnumSchema (removed) - do not reuse
}
