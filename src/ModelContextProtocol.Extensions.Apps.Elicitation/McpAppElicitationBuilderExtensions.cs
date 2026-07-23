using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Apps.Elicitation;

/// <summary>Builder extensions for app-rendered elicitation.</summary>
public static class McpAppElicitationBuilderExtensions
{
    /// <summary>Enables the app-elicitation extension and its required MCP Apps extension.</summary>
    public static IMcpServerBuilder WithMcpAppElicitation(this IMcpServerBuilder builder)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
#endif
        builder.WithMcpApps();
        builder.Services.AddSingleton<IPostConfigureOptions<McpServerOptions>, PostConfigureOptions>();
        return builder;
    }

    private sealed class PostConfigureOptions : IPostConfigureOptions<McpServerOptions>
    {
        public void PostConfigure(string? name, McpServerOptions options)
        {
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            if (!options.Capabilities.Extensions.ContainsKey(McpAppElicitation.ExtensionId))
            {
                options.Capabilities.Extensions[McpAppElicitation.ExtensionId] = new JsonObject
                {
                    ["requires"] = new JsonArray(McpApps.ExtensionId),
                };
            }
        }
    }
}
