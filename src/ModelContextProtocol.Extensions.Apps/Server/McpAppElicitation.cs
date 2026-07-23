using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Extensions.Apps;

/// <summary>Strongly typed conventions for using MCP Apps as form elicitation UI.</summary>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public static class McpAppElicitation
{
    /// <summary>The metadata member inherited from the MCP Apps extension.</summary>
    public const string UiMetaKey = "ui";

    /// <summary>Adds all client capabilities required for app-rendered form elicitation.</summary>
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

        if (!capabilities.Extensions.TryGetValue(McpApps.ExtensionId, out var value))
        {
            capabilities.Extensions[McpApps.ExtensionId] = new JsonObject
            {
                ["mimeTypes"] = new JsonArray(McpApps.HtmlMimeType),
                ["elicitation"] = new JsonObject(),
            };
        }
        else
        {
            var appCapabilities = value switch
            {
                McpUiClientCapabilities typed => JsonSerializer.SerializeToNode(
                    typed,
                    McpAppsJsonContext.Default.McpUiClientCapabilities)!.AsObject(),
                JsonObject jsonObject => jsonObject,
                JsonElement { ValueKind: JsonValueKind.Object } element => JsonNode.Parse(element.GetRawText())!.AsObject(),
                _ => new JsonObject(),
            };
            if (appCapabilities["mimeTypes"] is not JsonArray mimeTypes)
            {
                mimeTypes = new JsonArray();
                appCapabilities["mimeTypes"] = mimeTypes;
            }

            if (!mimeTypes.Any(node =>
                node is JsonValue valueNode &&
                valueNode.TryGetValue<string>(out var mimeType) &&
                string.Equals(mimeType, McpApps.HtmlMimeType, StringComparison.OrdinalIgnoreCase)))
            {
                mimeTypes.Add((JsonNode?)JsonValue.Create(McpApps.HtmlMimeType));
            }

            if (appCapabilities["elicitation"] is not JsonObject)
            {
                appCapabilities["elicitation"] = new JsonObject();
            }

            capabilities.Extensions[McpApps.ExtensionId] = appCapabilities;
        }

        return capabilities;
    }

    /// <summary>Returns whether the client advertised form elicitation and MCP Apps elicitation support.</summary>
    public static bool IsSupported(ClientCapabilities? capabilities)
    {
        var appCapabilities = McpApps.GetUiCapability(capabilities);
        return capabilities?.Elicitation?.Form is not null &&
            appCapabilities?.Elicitation is not null &&
            appCapabilities.MimeTypes?.Contains(McpApps.HtmlMimeType, StringComparer.OrdinalIgnoreCase) == true;
    }

    /// <summary>Associates an elicitation request with an MCP App UI resource.</summary>
    public static ElicitRequestParams SetAppUi(ElicitRequestParams request, string resourceUri)
    {
        ValidateAppUiArguments(request, resourceUri);

        request.Meta ??= [];
        request.Meta[UiMetaKey] = JsonSerializer.SerializeToNode(
            new McpAppElicitationMeta { ResourceUri = resourceUri },
            McpAppsJsonContext.Default.McpAppElicitationMeta);
        return request;
    }

    /// <summary>
    /// Associates an elicitation request with an MCP App UI resource when the client advertised all required
    /// capabilities. Otherwise, leaves the core elicitation unchanged for native form rendering.
    /// </summary>
    public static ElicitRequestParams SetAppUiIfSupported(
        ElicitRequestParams request,
        ClientCapabilities? capabilities,
        string resourceUri)
    {
        ValidateAppUiArguments(request, resourceUri);
        return IsSupported(capabilities) ? SetAppUi(request, resourceUri) : request;
    }

    /// <summary>
    /// Associates an elicitation request with an MCP App UI resource when the requesting client advertised all
    /// required capabilities. Uses request-scoped capabilities on 2026-07-28 and session capabilities on legacy
    /// stateful connections.
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
        var capabilities = context.JsonRpcRequest.Context?.ClientCapabilities ?? context.Server.ClientCapabilities;
        return SetAppUiIfSupported(request, capabilities, resourceUri);
    }

    private static void ValidateAppUiArguments(ElicitRequestParams request, string resourceUri)
    {
#if NET
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceUri);
#else
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(resourceUri)) throw new ArgumentException("The resource URI is required.", nameof(resourceUri));
#endif
        if (!Uri.TryCreate(resourceUri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "ui", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("MCP App elicitation resources must use the ui:// URI scheme.", nameof(resourceUri));
        }
    }

    /// <summary>Gets the app UI metadata from an elicitation request, if present and valid.</summary>
    public static McpAppElicitationMeta? GetAppUi(ElicitRequestParams request)
    {
#if NET
        ArgumentNullException.ThrowIfNull(request);
#else
        if (request is null) throw new ArgumentNullException(nameof(request));
#endif
        if (request.Meta?[UiMetaKey] is not JsonNode node)
        {
            return null;
        }

        try
        {
            return node.Deserialize(McpAppsJsonContext.Default.McpAppElicitationMeta);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns a typed elicitation response on an MRTR retry, or requests the app-rendered elicitation on the first round.
    /// </summary>
    /// <remarks>
    /// This explicit retry-safe convention is suitable for stateless HTTP. Any operation state needed after the
    /// retry must be encoded in the original request arguments or in <paramref name="requestState"/>.
    /// Clients that do not support MCP Apps elicitation render the requested schema natively.
    /// </remarks>
    public static ElicitResult<T> ResolveOrRequest<T>(
        McpServer server,
        RequestParams requestParams,
        string inputKey,
        ElicitRequestParams elicitation,
        JsonTypeInfo<T> responseTypeInfo,
        string? requestState = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(requestParams);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputKey);
        ArgumentNullException.ThrowIfNull(elicitation);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);
#else
        if (server is null) throw new ArgumentNullException(nameof(server));
        if (requestParams is null) throw new ArgumentNullException(nameof(requestParams));
        if (string.IsNullOrWhiteSpace(inputKey)) throw new ArgumentException("The input key is required.", nameof(inputKey));
        if (elicitation is null) throw new ArgumentNullException(nameof(elicitation));
        if (responseTypeInfo is null) throw new ArgumentNullException(nameof(responseTypeInfo));
#endif

        if (requestParams.InputResponses?.TryGetValue(inputKey, out var response) == true)
        {
            var raw = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo)
                ?? throw new McpProtocolException($"The '{inputKey}' elicitation response was empty.", McpErrorCode.InvalidParams);

            if (!raw.IsAccepted || raw.Content is null)
            {
                return new ElicitResult<T> { Action = raw.Action };
            }

            JsonObject content = [];
            foreach (var item in raw.Content)
            {
                content[item.Key] = JsonNode.Parse(item.Value.GetRawText());
            }

            var typed = JsonSerializer.Deserialize(content, responseTypeInfo);
            return new ElicitResult<T> { Action = raw.Action, Content = typed };
        }

        if (requestParams.InputResponses is { Count: > 0 })
        {
            throw new McpProtocolException($"The MRTR retry did not contain the expected '{inputKey}' response.", McpErrorCode.InvalidParams);
        }

        if (!server.IsMrtrSupported)
        {
            throw new InvalidOperationException("App elicitation requires an MRTR-capable client or a stateful transport.");
        }

        throw new InputRequiredException(
            new Dictionary<string, InputRequest>
            {
                [inputKey] = InputRequest.ForElicitation(elicitation),
            },
            requestState);
    }
}
