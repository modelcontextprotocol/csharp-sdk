using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol;

/// <summary>
/// Defines diagnostic IDs, messages, and URLs for APIs annotated with <see cref="ExperimentalAttribute"/>.
/// </summary>
/// <remarks>
/// Experimental diagnostic IDs are grouped by category:
/// <list type="bullet">
/// <item><description>
/// <c>MCPEXP001</c> covers APIs related to experimental features in the MCP specification itself.
/// These APIs may change as the specification evolves.
/// </description></item>
/// <item><description>
/// <c>MCPEXP002</c> covers SDK extensibility APIs that enable features to be implemented
/// in standalone packages without requiring Core to understand those features. These APIs
/// remain experimental until additional extensibility scenarios validate the design.
/// </description></item>
/// </list>
/// <para>
/// When an experimental API is associated with an experimental specification, the message
/// should refer to the specification version that introduces the feature and the SEP
/// when available. If there is a SEP associated with the experimental API, the Url should
/// point to the SEP issue.
/// </para>
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
    /// Diagnostic ID for experimental MCP specification features.
    /// </summary>
    /// <remarks>
    /// When introducing a new experimental specification feature, add feature-specific message
    /// and URL constants that use this diagnostic ID.
    /// </remarks>
    public const string SpecificationFeature_DiagnosticId = "MCPEXP001";

    /// <summary>
    /// Diagnostic ID for experimental MCP Apps extension APIs.
    /// </summary>
    /// <remarks>
    /// MCP Apps is the first official MCP extension (<c>"io.modelcontextprotocol/ui"</c>), enabling
    /// servers to deliver interactive UIs inside AI clients. This uses a dedicated diagnostic ID
    /// so that users can suppress it independently from other experimental APIs.
    /// </remarks>
    public const string Apps_DiagnosticId = "MCPEXP003";

    /// <summary>
    /// Message for the experimental MCP Apps extension APIs.
    /// </summary>
    public const string Apps_Message = "The MCP Apps extension is experimental and subject to change as the specification evolves.";

    /// <summary>
    /// URL for the experimental MCP Apps extension APIs.
    /// </summary>
    public const string Apps_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#mcpexp003";

    /// <summary>
    /// Diagnostic ID for SDK extensibility APIs that enable independently packaged features,
    /// such as Tasks, without requiring Core awareness of those features.
    /// </summary>
    /// <remarks>
    /// This diagnostic ID covers experimental SDK-level extensibility APIs. All constants
    /// in this group share the same diagnostic ID so users need only one suppression point
    /// for SDK design preview features.
    /// </remarks>
    public const string Extensibility_DiagnosticId = "MCPEXP002";

    /// <summary>
    /// Message for experimental extensibility points in the C# SDK implementation.
    /// </summary>
    public const string Extensibility_Message = "This C# SDK extensibility API is experimental and subject to change.";

    /// <summary>
    /// URL for experimental extensibility points in the C# SDK implementation.
    /// </summary>
    public const string Extensibility_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#mcpexp002";

    /// <summary>
    /// Diagnostic ID for the experimental <c>RunSessionHandler</c> API.
    /// </summary>
    /// <remarks>
    /// This uses the same diagnostic ID as <see cref="Extensibility_DiagnosticId"/> because
    /// both are experimental SDK APIs unrelated to the MCP specification.
    /// </remarks>
    public const string RunSessionHandler_DiagnosticId = "MCPEXP002";

    /// <summary>
    /// Message for the experimental <c>RunSessionHandler</c> API.
    /// </summary>
    public const string RunSessionHandler_Message = "RunSessionHandler is experimental and may be removed or changed in a future release. Consider using ConfigureSessionOptions instead.";

    /// <summary>
    /// URL for the experimental <c>RunSessionHandler</c> API.
    /// </summary>
    public const string RunSessionHandler_Url = "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/list-of-diagnostics.md#mcpexp002";

}
