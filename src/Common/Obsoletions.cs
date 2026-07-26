namespace ModelContextProtocol;

/// <summary>
/// Defines diagnostic IDs, messages, and URLs for APIs annotated with <see cref="ObsoleteAttribute"/>.
/// </summary>
/// <remarks>
/// When a deprecated API is associated with a specification change, the message
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

    // MCP9002 was used for the AddXxxFilter extension methods on IMcpServerBuilder that were superseded by
    // WithMessageFilters() and WithRequestFilters(). The APIs were removed; do not reuse this diagnostic ID.

    public const string RequestContextParamsConstructor_DiagnosticId = "MCP9003";
    public const string RequestContextParamsConstructor_Message = "Use the constructor overload that accepts a parameters argument.";
    public const string RequestContextParamsConstructor_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#obsolete-apis";

    public const string EnableLegacySse_DiagnosticId = "MCP9004";
    public const string EnableLegacySse_Message = "Legacy SSE transport has no built-in request backpressure and should only be used with completely trusted clients in isolated processes. Use Streamable HTTP instead.";
    public const string EnableLegacySse_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#obsolete-apis";

    // SEP-2577 deprecates the Roots, Sampling, and Logging features as a single coordinated
    // deprecation. They share one diagnostic ID (MCP9005) so consumers can opt out with a single
    // suppression, while the feature-specific messages keep the diagnostics distinguishable.
    public const string Deprecated_DiagnosticId = "MCP9005";
    public const string Deprecated_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#obsolete-apis";
    public const string DeprecatedRoots_Message = "The Roots feature is deprecated as of specification version 2026-07-28 and may be removed in a future version. See SEP-2577 for more information.";
    public const string DeprecatedSampling_Message = "The Sampling feature is deprecated as of specification version 2026-07-28 and may be removed in a future version. See SEP-2577 for more information.";
    public const string DeprecatedLogging_Message = "The Logging feature is deprecated as of specification version 2026-07-28 and may be removed in a future version. See SEP-2577 for more information.";

    public const string LegacyStatefulHttp_DiagnosticId = "MCP9006";
    public const string LegacyStatefulHttp_Message = "Stateful Streamable HTTP mode is a back-compat-only escape hatch for legacy clients. Set HttpServerTransportOptions.Stateless = true (the default as of the 2026-07-28 protocol revision) for new code. See SEP-2567.";
    public const string LegacyStatefulHttp_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#obsolete-apis";

    public const string AuthorizationRedirectDelegate_DiagnosticId = "MCP9007";
    public const string AuthorizationRedirectDelegate_Message = "AuthorizationRedirectDelegate cannot provide the RFC 9207 issuer and is retained for compatibility only. Use AuthorizationCallbackHandler instead.";
    public const string AuthorizationRedirectDelegate_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#obsolete-apis";
}
