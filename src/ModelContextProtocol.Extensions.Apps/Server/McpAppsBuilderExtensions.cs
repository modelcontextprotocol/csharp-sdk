using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Extensions.Apps;

/// <summary>
/// Extension methods for <see cref="IMcpServerBuilder"/> to enable MCP Apps support.
/// </summary>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public static class McpAppsBuilderExtensions
{
    /// <summary>
    /// Registers a tool together with the HTML resource it renders.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="method">The tool method to expose.</param>
    /// <param name="resourceUri">The <c>ui://</c> resource URI associated with the tool.</param>
    /// <param name="htmlFactory">A callback that returns the HTML for the UI resource.</param>
    /// <param name="toolOptions">Optional options used when creating the tool.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/>, <paramref name="method"/>, <paramref name="resourceUri"/>, or
    /// <paramref name="htmlFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="resourceUri"/> is empty or consists only of whitespace.</exception>
    /// <remarks>
    /// <para>
    /// This is the compact equivalent of creating a tool with <see cref="McpServerTool.Create(Delegate, McpServerToolCreateOptions?)"/>,
    /// applying <see cref="McpApps.SetAppUi(McpServerTool, McpUiToolMeta)"/>, and creating a resource with
    /// <see cref="McpServerResource.Create(Delegate, McpServerResourceCreateOptions?)"/>. The resource is registered with
    /// <see cref="McpApps.HtmlMimeType"/>, and the returned HTML is wrapped by the existing resource result conversion.
    /// </para>
    /// <para>
    /// Calling this method also enables <see cref="WithMcpApps(IMcpServerBuilder)"/> so the server advertises MCP Apps support.
    /// Existing <see cref="McpServerToolCreateOptions.Meta"/> UI metadata is preserved, as it is with
    /// <see cref="McpApps.SetAppUi(McpServerTool, McpUiToolMeta)"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// builder.Services
    ///     .AddMcpServer()
    ///     .WithAppTool(
    ///         (string location) =&gt; $&quot;Weather for {location}&quot;,
    ///         &quot;ui://weather/view.html&quot;,
    ///         () =&gt; File.ReadAllText(&quot;weather.html&quot;));
    /// </code>
    /// </example>
    public static IMcpServerBuilder WithAppTool(
        this IMcpServerBuilder builder,
        Delegate method,
        string resourceUri,
        Func<string> htmlFactory,
        McpServerToolCreateOptions? toolOptions = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(resourceUri);
        ArgumentNullException.ThrowIfNull(htmlFactory);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (method is null) throw new ArgumentNullException(nameof(method));
        if (resourceUri is null) throw new ArgumentNullException(nameof(resourceUri));
        if (htmlFactory is null) throw new ArgumentNullException(nameof(htmlFactory));
#endif
        if (string.IsNullOrWhiteSpace(resourceUri))
        {
            throw new ArgumentException("Value cannot be empty or composed entirely of whitespace.", nameof(resourceUri));
        }

        var tool = McpApps.SetAppUi(
            McpServerTool.Create(method, toolOptions),
            new McpUiToolMeta { ResourceUri = resourceUri });
        var resource = McpServerResource.Create(
            htmlFactory,
            new McpServerResourceCreateOptions
            {
                UriTemplate = resourceUri,
                MimeType = McpApps.HtmlMimeType,
            });

        return builder
            .WithTools([tool])
            .WithResources([resource])
            .WithMcpApps();
    }

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
                    McpApps.ApplyAppUiAttributes(tool);
                }
            }
        }
    }
}
