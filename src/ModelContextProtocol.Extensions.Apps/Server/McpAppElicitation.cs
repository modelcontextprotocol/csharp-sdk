using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Apps;

/// <summary>Provides conventions for using MCP Apps as form elicitation UI.</summary>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public static partial class McpAppElicitation
{
    /// <summary>Adds the client capabilities required for app-rendered form elicitation.</summary>
    public static ClientCapabilities AddClientCapabilities(ClientCapabilities capabilities)
    {
#if NET
        ArgumentNullException.ThrowIfNull(capabilities);
#else
        if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));
#endif

        capabilities.Elicitation ??= new ElicitationCapability();
        capabilities.Elicitation.Form ??= new FormElicitationCapability();
        capabilities.Extensions ??= new Dictionary<string, object>();

        JsonObject uiCapabilities;
        if (capabilities.Extensions.TryGetValue(McpApps.ExtensionId, out var existing))
        {
            uiCapabilities = existing switch
            {
                McpUiClientCapabilities typed => JsonSerializer.SerializeToNode(
                    typed,
                    McpAppsJsonContext.Default.McpUiClientCapabilities)!.AsObject(),
                JsonObject jsonObject => jsonObject,
                JsonElement { ValueKind: JsonValueKind.Object } element =>
                    JsonNode.Parse(element.GetRawText())!.AsObject(),
                _ => [],
            };
        }
        else
        {
            uiCapabilities = [];
        }

        if (uiCapabilities["mimeTypes"] is not JsonArray mimeTypes)
        {
            mimeTypes = [];
            uiCapabilities["mimeTypes"] = mimeTypes;
        }

        if (!mimeTypes.Any(node =>
            node is JsonValue value &&
            value.TryGetValue<string>(out var mimeType) &&
            string.Equals(mimeType, McpApps.HtmlMimeType, StringComparison.OrdinalIgnoreCase)))
        {
            mimeTypes.Add((JsonNode?)JsonValue.Create(McpApps.HtmlMimeType));
        }

        uiCapabilities["elicitation"] ??= new JsonObject();
        capabilities.Extensions[McpApps.ExtensionId] = uiCapabilities;
        return capabilities;
    }

    /// <summary>
    /// Returns whether both peers advertised app-rendered form elicitation
    /// through the existing MCP Apps extension.
    /// </summary>
    public static bool IsSupported(
        ClientCapabilities? clientCapabilities,
        ServerCapabilities? serverCapabilities)
    {
        var serverUi = McpApps.GetUiServerCapability(serverCapabilities);

        return IsClientSupported(clientCapabilities) &&
            serverUi?.Elicitation is not null;
    }

    /// <summary>
    /// Returns whether a client advertised all capabilities required for
    /// app-rendered form elicitation.
    /// </summary>
    /// <remarks>
    /// A server using this one-sided check must also advertise
    /// <c>io.modelcontextprotocol/ui.elicitation</c>, as <see cref="McpAppsBuilderExtensions.WithMcpApps"/>
    /// does.
    /// </remarks>
    public static bool IsClientSupported(ClientCapabilities? clientCapabilities)
    {
        var clientUi = McpApps.GetUiCapability(clientCapabilities);
        return clientCapabilities?.Elicitation?.Form is not null &&
            clientUi?.Elicitation is not null &&
            clientUi.MimeTypes?.Contains(McpApps.HtmlMimeType, StringComparer.OrdinalIgnoreCase) == true;
    }

    /// <summary>Associates a form elicitation request with an MCP App UI resource.</summary>
    public static ElicitRequestParams SetAppUi(ElicitRequestParams request, string resourceUri)
    {
        ValidateArguments(request, resourceUri);

        request.Meta ??= [];
        request.Meta["ui"] = JsonSerializer.SerializeToNode(
            new McpAppElicitationMeta { ResourceUri = resourceUri },
            McpAppsJsonContext.Default.McpAppElicitationMeta);
        return request;
    }

    /// <summary>
    /// Associates a form elicitation with an MCP App only when both peers
    /// negotiated support. Otherwise the request remains a native elicitation.
    /// </summary>
    public static ElicitRequestParams SetAppUiIfSupported(
        ElicitRequestParams request,
        ClientCapabilities? clientCapabilities,
        ServerCapabilities? serverCapabilities,
        string resourceUri)
    {
        ValidateArguments(request, resourceUri);
        return IsSupported(clientCapabilities, serverCapabilities)
            ? SetAppUi(request, resourceUri)
            : request;
    }

    /// <summary>
    /// Associates a form elicitation with an MCP App when the client negotiated
    /// support. The server must advertise its side of the setting by calling
    /// <see cref="McpAppsBuilderExtensions.WithMcpApps"/>.
    /// </summary>
    public static ElicitRequestParams SetAppUiIfSupported(
        ElicitRequestParams request,
        ClientCapabilities? clientCapabilities,
        string resourceUri)
    {
        ValidateArguments(request, resourceUri);
        return IsClientSupported(clientCapabilities)
            ? SetAppUi(request, resourceUri)
            : request;
    }

    /// <summary>
    /// Associates a form elicitation with an MCP App using the requesting
    /// client's request-scoped capabilities when available. Protocol revision
    /// <c>2026-07-28</c> supplies these capabilities on every request; older
    /// revisions fall back to the initialized session capabilities.
    /// </summary>
    public static ElicitRequestParams SetAppUiIfSupported<TParams>(
        ElicitRequestParams request,
        RequestContext<TParams> context,
        string resourceUri)
    {
#if NET
        ArgumentNullException.ThrowIfNull(context);
#else
        if (context is null) throw new ArgumentNullException(nameof(context));
#endif

        var clientCapabilities =
            context.JsonRpcRequest.Context?.ClientCapabilities ??
            context.Server.ClientCapabilities;
        return SetAppUiIfSupported(request, clientCapabilities, resourceUri);
    }

    /// <summary>Gets the MCP App UI metadata from an elicitation request.</summary>
    public static McpAppElicitationMeta? GetAppUi(ElicitRequestParams request)
    {
#if NET
        ArgumentNullException.ThrowIfNull(request);
#else
        if (request is null) throw new ArgumentNullException(nameof(request));
#endif

        if (request.Meta?["ui"] is not JsonNode node)
        {
            return null;
        }

        try
        {
            var result = node.Deserialize(McpAppsJsonContext.Default.McpAppElicitationMeta);
            return result is not null && IsAbsoluteUiUri(result.ResourceUri) ? result : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateArguments(ElicitRequestParams request, string resourceUri)
    {
#if NET
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceUri);
#else
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(resourceUri)) throw new ArgumentException("The resource URI is required.", nameof(resourceUri));
#endif

        if (!string.Equals(request.Mode, "form", StringComparison.Ordinal))
        {
            throw new ArgumentException("MCP Apps only support form-mode elicitations.", nameof(request));
        }

        if (!IsAbsoluteUiUri(resourceUri))
        {
            throw new ArgumentException(
                "MCP App elicitation resources must be absolute ui:// URIs.",
                nameof(resourceUri));
        }
    }

    private static bool IsAbsoluteUiUri(string resourceUri) =>
        Uri.TryCreate(resourceUri, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, "ui", StringComparison.OrdinalIgnoreCase);
}
