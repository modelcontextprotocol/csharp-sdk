using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Internal helper methods for MCP Apps integration within the Core package.
/// The public MCP Apps API surface is in the ModelContextProtocol.ExtApps package.
/// </summary>
internal static class McpAppsInternal
{
    /// <summary>
    /// Applies UI tool metadata to a <see cref="System.Text.Json.Nodes.JsonObject"/>, setting the
    /// <c>ui</c> object key if not already present.
    /// </summary>
    /// <param name="appUi">The UI tool metadata to apply.</param>
    /// <param name="meta">The <see cref="System.Text.Json.Nodes.JsonObject"/> to populate.</param>
    internal static void ApplyUiToolMetaToJsonObject(McpUiToolMeta appUi, System.Text.Json.Nodes.JsonObject meta)
    {
        // Populate the structured "ui" object if not already present.
        if (!meta.ContainsKey("ui"))
        {
            var uiNode = JsonSerializer.SerializeToNode(appUi, McpJsonUtilities.JsonContext.Default.McpUiToolMeta);
            if (uiNode is not null)
            {
                meta["ui"] = uiNode;
            }
        }
    }
}
