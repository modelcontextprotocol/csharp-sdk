using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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
        if (builder is null) throw new ArgumentNullException(nameof(builder));
#endif

        builder.Services.AddSingleton<IPostConfigureOptions<McpServerOptions>, McpAppsPostConfigureOptions>();
        return builder;
    }

    private sealed class McpAppsPostConfigureOptions : IPostConfigureOptions<McpServerOptions>
    {
        /// <summary>The tools whose <see cref="McpAppUiAttribute"/> has already been applied.</summary>
        /// <remarks>
        /// Options can be created many times, for example once per request by a stateless HTTP server, and registered
        /// tools are typically shared between them, or even between service providers. Applying the attribute once per
        /// tool avoids repeating the work and avoids concurrently mutating a shared tool's metadata. Tools are held
        /// weakly so that tools created for a single session aren't kept alive.
        /// </remarks>
        private static readonly ConditionalWeakTable<McpServerTool, object> s_processedTools = new();

        public void PostConfigure(string? name, McpServerOptions options)
        {
            // Advertise server-side MCP Apps support in capabilities.
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            if (!options.Capabilities.Extensions.ContainsKey(McpApps.ExtensionId))
            {
                options.Capabilities.Extensions[McpApps.ExtensionId] = new System.Text.Json.Nodes.JsonObject();
            }

            if (options.ToolCollection is { IsEmpty: false } tools)
            {
                foreach (var tool in tools)
                {
                    if (!s_processedTools.TryGetValue(tool, out _))
                    {
                        lock (s_processedTools)
                        {
                            if (!s_processedTools.TryGetValue(tool, out _))
                            {
                                McpApps.ApplyAppUiAttributes(tool);
                                s_processedTools.Add(tool, s_processedTools);
                            }
                        }
                    }
                }
            }
        }
    }
}
