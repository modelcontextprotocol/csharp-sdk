using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Extensions.Apps;

/// <summary>
/// Extension methods for <see cref="IMcpServerBuilder"/> to enable MCP Apps support.
/// </summary>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public static class McpAppsBuilderExtensions
{
    /// <summary>
    /// Enables MCP Apps support by automatically processing <see cref="McpAppUiAttribute"/> on registered tools.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>
    /// Call this method after registering tools (e.g., after <c>WithTools&lt;T&gt;()</c>) to automatically
    /// apply <see cref="McpAppUiAttribute"/> metadata to the tool's <c>_meta.ui</c> field.
    /// </para>
    /// <para>
    /// Tools that already have a <c>ui</c> key in their <see cref="Protocol.Tool.Meta"/> (e.g., set explicitly
    /// via <see cref="McpServerToolCreateOptions.Meta"/>) are not modified.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// builder.Services
    ///     .AddMcpServer()
    ///     .WithTools&lt;MyToolType&gt;()
    ///     .WithMcpApps();
    /// </code>
    /// </example>
    public static IMcpServerBuilder WithMcpApps(this IMcpServerBuilder builder)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
#else
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));
#endif

        builder.Services.AddSingleton<
            IPostConfigureOptions<McpServerOptions>,
            McpAppsPostConfigureOptions
        >();
        return builder;
    }

    private sealed class McpAppsPostConfigureOptions : IPostConfigureOptions<McpServerOptions>
    {
        public void PostConfigure(string? name, McpServerOptions options)
        {
            // Advertise server-side MCP Apps support in capabilities.
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            if (options.Capabilities.Extensions.TryGetValue(McpApps.ExtensionId, out var existing))
            {
                switch (existing)
                {
                    case McpUiServerCapabilities typed:
                        typed.Elicitation ??= new McpUiElicitationCapability();
                        break;
                    case JsonObject jsonObject:
                        jsonObject["elicitation"] ??= new JsonObject();
                        break;
                    case JsonElement { ValueKind: JsonValueKind.Object } element:
                        var merged = JsonNode.Parse(element.GetRawText())!.AsObject();
                        merged["elicitation"] ??= new JsonObject();
                        options.Capabilities.Extensions[McpApps.ExtensionId] = merged;
                        break;
                }
            }
            else
            {
                options.Capabilities.Extensions[McpApps.ExtensionId] = new JsonObject
                {
                    ["elicitation"] = new JsonObject(),
                };
            }

            if (options.ToolCollection is { IsEmpty: false } tools)
            {
                foreach (var tool in tools)
                {
                    McpApps.ApplyAppUiAttributes(tool);
                }
            }
        }
    }
}
