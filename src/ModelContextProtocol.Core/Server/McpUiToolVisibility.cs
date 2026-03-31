using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides well-known visibility values for <see cref="McpUiToolMeta.Visibility"/>.
/// </summary>
/// <remarks>
/// Use these constants to specify which principals can invoke a tool in the MCP Apps extension.
/// When <see cref="McpUiToolMeta.Visibility"/> is <see langword="null"/> or empty, the tool
/// is visible to both the model and the app by default.
/// </remarks>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public static class McpUiToolVisibility
{
    /// <summary>
    /// Indicates that the tool can be invoked by the AI model.
    /// </summary>
    public const string Model = "model";

    /// <summary>
    /// Indicates that the tool can be invoked by the UI app (iframe).
    /// </summary>
    public const string App = "app";
}
