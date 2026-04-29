using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

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
/// <para>
/// Use <see cref="SetAppUi"/> to set the <c>_meta.ui</c> metadata on a tool, or
/// <see cref="ApplyAppUiAttributes(IEnumerable{McpServerTool})"/> to automatically process
/// <see cref="McpAppUiAttribute"/> instances on tools created from methods.
/// </para>
/// </remarks>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
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
    /// Gets the <see cref="JsonSerializerOptions"/> configured with source-generated metadata
    /// for MCP Apps extension types.
    /// </summary>
    /// <remarks>
    /// Use these options when serializing or deserializing MCP Apps types such as
    /// <see cref="McpUiToolMeta"/>, <see cref="McpUiClientCapabilities"/>, and <see cref="McpUiResourceMeta"/>.
    /// </remarks>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Insert(0, McpAppsJsonContext.Default);
        options.MakeReadOnly();
        return options;
    }

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
                JsonSerializer.Deserialize(element, McpAppsJsonContext.Default.McpUiClientCapabilities);
        }

        return null;
    }

    /// <summary>
    /// Sets the MCP Apps UI metadata on a tool's <see cref="Tool.Meta"/> property.
    /// </summary>
    /// <param name="tool">The tool to set the UI metadata on.</param>
    /// <param name="appUi">The UI metadata to apply.</param>
    /// <returns>The same <paramref name="tool"/> instance, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method sets the <c>ui</c> key in the tool's <see cref="Tool.Meta"/> object.
    /// If a <c>ui</c> key is already present in <see cref="Tool.Meta"/>, it is not overwritten.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> or <paramref name="appUi"/> is <see langword="null"/>.</exception>
    public static McpServerTool SetAppUi(McpServerTool tool, McpUiToolMeta appUi)
    {
#if NET
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(appUi);
#else
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (appUi is null) throw new ArgumentNullException(nameof(appUi));
#endif

        var protocolTool = tool.ProtocolTool;
        protocolTool.Meta ??= new JsonObject();

        if (!protocolTool.Meta.ContainsKey("ui"))
        {
            var uiNode = JsonSerializer.SerializeToNode(appUi, McpAppsJsonContext.Default.McpUiToolMeta);
            if (uiNode is not null)
            {
                protocolTool.Meta["ui"] = uiNode;
            }
        }

        return tool;
    }

    /// <summary>
    /// Processes a collection of tools, applying <see cref="McpAppUiAttribute"/> metadata to any
    /// tool whose underlying method has the attribute.
    /// </summary>
    /// <param name="tools">The tools to process.</param>
    /// <returns>The same <paramref name="tools"/> enumerable, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// For each tool that has a <see cref="McpAppUiAttribute"/> in its <see cref="McpServerTool.Metadata"/>,
    /// this method sets the <c>ui</c> key in the tool's <see cref="Tool.Meta"/> if not already present.
    /// </para>
    /// <para>
    /// If <see cref="Tool.Meta"/> already contains a <c>ui</c> key (e.g., set explicitly via
    /// <see cref="McpServerToolCreateOptions.Meta"/>), the attribute is not applied.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tools"/> is <see langword="null"/>.</exception>
    public static IEnumerable<McpServerTool> ApplyAppUiAttributes(IEnumerable<McpServerTool> tools)
    {
#if NET
        ArgumentNullException.ThrowIfNull(tools);
#else
        if (tools is null) throw new ArgumentNullException(nameof(tools));
#endif

        foreach (var tool in tools)
        {
            ApplyAppUiAttributes(tool);
        }

        return tools;
    }

    /// <summary>
    /// Processes a single tool, applying <see cref="McpAppUiAttribute"/> metadata if the tool's
    /// underlying method has the attribute.
    /// </summary>
    /// <param name="tool">The tool to process.</param>
    /// <returns>The same <paramref name="tool"/> instance, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// If the tool has a <see cref="McpAppUiAttribute"/> in its <see cref="McpServerTool.Metadata"/>,
    /// this method sets the <c>ui</c> key in the tool's <see cref="Tool.Meta"/> if not already present.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    public static McpServerTool ApplyAppUiAttributes(McpServerTool tool)
    {
#if NET
        ArgumentNullException.ThrowIfNull(tool);
#else
        if (tool is null) throw new ArgumentNullException(nameof(tool));
#endif

        // Look for McpAppUiAttribute in tool metadata (attributes from the method)
        foreach (var metadataItem in tool.Metadata)
        {
            if (metadataItem is McpAppUiAttribute appUiAttr)
            {
                var meta = new McpUiToolMeta
                {
                    ResourceUri = appUiAttr.ResourceUri,
                    Visibility = appUiAttr.Visibility,
                };

                SetAppUi(tool, meta);
                break;
            }
        }

        return tool;
    }
}
