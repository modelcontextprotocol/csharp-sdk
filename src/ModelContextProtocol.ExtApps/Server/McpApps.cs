using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

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
                (McpUiClientCapabilities?)JsonSerializer.Deserialize(
                    element,
                    (JsonTypeInfo)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(McpUiClientCapabilities)));
        }

        return null;
    }
}
