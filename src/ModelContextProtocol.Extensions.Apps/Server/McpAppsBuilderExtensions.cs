using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

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
    /// <param name="resourceUri">The absolute <c>ui://</c> resource URI associated with the tool.</param>
    /// <param name="htmlFactory">A resource handler that returns the HTML, synchronously or asynchronously.</param>
    /// <param name="toolOptions">Optional options used when creating the tool.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/>, <paramref name="method"/>, <paramref name="resourceUri"/>, or
    /// <paramref name="htmlFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resourceUri"/> is not an absolute, non-templated <c>ui://</c> URI, or conflicts with the
    /// tool's existing <c>_meta.ui.resourceUri</c> value.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is the compact equivalent of creating a tool with <see cref="McpServerTool.Create(Delegate, McpServerToolCreateOptions?)"/>,
    /// applying <see cref="McpApps.SetAppUi(McpServerTool, McpUiToolMeta)"/>, and creating a resource with
    /// <see cref="McpServerResource.Create(Delegate, McpServerResourceCreateOptions?)"/>. The resource is registered with
    /// <see cref="McpApps.HtmlMimeType"/>, and the returned HTML is wrapped by the existing resource result conversion.
    /// </para>
    /// <para>
    /// Calling this method also enables <see cref="WithMcpApps(IMcpServerBuilder)"/> so the server advertises MCP Apps support.
    /// Existing <see cref="McpServerToolCreateOptions.Meta"/> UI metadata is preserved. If it already contains
    /// <c>ui.resourceUri</c>, that value must exactly match <paramref name="resourceUri"/>.
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
        Delegate htmlFactory,
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
        if (resourceUri.Contains('{') || resourceUri.Contains('}'))
        {
            throw new ArgumentException("The resource URI must identify a concrete UI resource and cannot be a URI template.", nameof(resourceUri));
        }

        if (string.IsNullOrWhiteSpace(resourceUri) ||
            !Uri.TryCreate(resourceUri, UriKind.Absolute, out Uri? parsedUri) ||
            !parsedUri.IsWellFormedOriginalString() ||
            !parsedUri.Scheme.Equals("ui", StringComparison.OrdinalIgnoreCase) ||
            !resourceUri.StartsWith("ui://", StringComparison.OrdinalIgnoreCase) ||
            (parsedUri.Host.Length == 0 && parsedUri.AbsolutePath.Length <= 1))
        {
            throw new ArgumentException("The resource URI must be a valid absolute URI using the ui:// scheme.", nameof(resourceUri));
        }

        var tool = McpApps.SetAppUi(
            McpServerTool.Create(method, toolOptions),
            new McpUiToolMeta { ResourceUri = resourceUri });

        if (tool.ProtocolTool.Meta?["ui"] is not JsonObject uiMetadata)
        {
            throw new ArgumentException("The tool's _meta.ui value must be an object.", nameof(resourceUri));
        }

        if (uiMetadata["resourceUri"] is { } resourceUriNode)
        {
            if (resourceUriNode is not JsonValue resourceUriValue ||
                !resourceUriValue.TryGetValue(out string? existingResourceUri))
            {
                throw new ArgumentException("The tool's _meta.ui.resourceUri value must be a string.", nameof(resourceUri));
            }

            if (!string.Equals(existingResourceUri, resourceUri, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The tool's UI resource URI '{existingResourceUri}' does not match the registered resource URI '{resourceUri}'.",
                    nameof(resourceUri));
            }
        }

        uiMetadata["resourceUri"] = resourceUri;

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
