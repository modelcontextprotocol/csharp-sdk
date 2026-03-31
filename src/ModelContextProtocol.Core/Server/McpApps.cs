using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides constants and helper methods for building MCP Apps-enabled servers.
/// </summary>
/// <remarks>
/// <para>
/// MCP Apps is an extension to the Model Context Protocol that enables MCP servers to deliver
/// interactive user interfaces — dashboards, forms, visualizations, and more — directly inside
/// conversational AI clients.
/// </para>
/// <para>
/// Use the constants in this class when populating the <c>extensions</c> capability and the
/// <c>_meta</c> field of tools and resources. Use <see cref="GetUiCapability"/> to check whether
/// the connected client supports the MCP Apps extension.
/// </para>
/// </remarks>
public static class McpApps
{
    /// <summary>
    /// The MIME type used for MCP App HTML resources.
    /// </summary>
    /// <remarks>
    /// This MIME type should be used when registering UI resources with
    /// <c>text/html;profile=mcp-app</c> to indicate they are MCP App resources.
    /// </remarks>
    public const string ResourceMimeType = "text/html;profile=mcp-app";

    /// <summary>
    /// The extension identifier used for MCP Apps capability negotiation.
    /// </summary>
    /// <remarks>
    /// This key is used in the <see cref="ClientCapabilities.Extensions"/> and
    /// <see cref="ServerCapabilities.Extensions"/> dictionaries to advertise support for
    /// the MCP Apps extension.
    /// </remarks>
    public const string ExtensionId = "io.modelcontextprotocol/ui";

    /// <summary>
    /// The legacy flat <c>_meta</c> key for the UI resource URI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This key is used for backward compatibility with older MCP hosts that do not support
    /// the nested <c>_meta.ui</c> object. When populating UI metadata, both this key and the
    /// <c>ui</c> object should be set to the same resource URI value.
    /// </para>
    /// <para>
    /// This key is considered legacy; prefer <see cref="McpUiToolMeta.ResourceUri"/> for new implementations.
    /// </para>
    /// </remarks>
    public const string ResourceUriMetaKey = "ui/resourceUri";

    /// <summary>
    /// Gets the MCP Apps client capability, if advertised by the connected client.
    /// </summary>
    /// <param name="capabilities">The client capabilities received during the MCP initialize handshake.</param>
    /// <returns>
    /// A <see cref="McpUiClientCapabilities"/> instance if the client advertises support for the MCP Apps extension;
    /// otherwise, <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Use this method to determine whether the connected client supports the MCP Apps extension
    /// and to read the client's supported MIME types.
    /// </remarks>
    [Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
    public static McpUiClientCapabilities? GetUiCapability(ClientCapabilities? capabilities)
    {
        if (capabilities?.Extensions is not { } extensions ||
            !extensions.TryGetValue(ExtensionId, out var value))
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Null ? null :
                JsonSerializer.Deserialize(element, McpJsonUtilities.JsonContext.Default.McpUiClientCapabilities);
        }

        return null;
    }

    /// <summary>
    /// Applies UI tool metadata to a <see cref="System.Text.Json.Nodes.JsonObject"/>, setting both the
    /// <c>ui</c> object key and the legacy <c>ui/resourceUri</c> flat key for backward compatibility.
    /// Keys already present in <paramref name="meta"/> are not overwritten.
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

        // Populate the legacy flat "ui/resourceUri" key if not already present.
        if (!meta.ContainsKey(ResourceUriMetaKey) && appUi.ResourceUri is not null)
        {
            meta[ResourceUriMetaKey] = appUi.ResourceUri;
        }
    }
}
