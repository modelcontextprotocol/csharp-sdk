using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// <param name="htmlFactory">
    /// A resource handler that returns the HTML as a <see cref="string"/>, <see cref="Task{TResult}"/> of
    /// <see cref="string"/>, or <see cref="ValueTask{TResult}"/> of <see cref="string"/>. It may have no parameters
    /// or a single <see cref="CancellationToken"/> parameter.
    /// </param>
    /// <param name="toolOptions">Optional options used when creating the tool.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/>, <paramref name="method"/>, <paramref name="resourceUri"/>, or
    /// <paramref name="htmlFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resourceUri"/> is not an absolute, non-templated <c>ui://</c> URI,
    /// <paramref name="htmlFactory"/> has an unsupported signature, or the tool's existing
    /// <c>_meta.ui.resourceUri</c> value is not a string that exactly matches it.
    /// </exception>
    /// <exception cref="OptionsValidationException">
    /// The app tool's name or resource URI conflicts with another registration when server options are resolved.
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
    /// <c>ui.resourceUri</c>, that value must be a string that exactly matches <paramref name="resourceUri"/>.
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

        ValidateHtmlFactory(htmlFactory);

        var tool = McpApps.SetAppUi(
            McpServerTool.Create(method, toolOptions),
            new McpUiToolMeta { ResourceUri = resourceUri });

        if (tool.ProtocolTool.Meta?["ui"] is not JsonObject uiMetadata)
        {
            throw new ArgumentException("The tool's _meta.ui value must be an object.", nameof(toolOptions));
        }

        if (uiMetadata.ContainsKey("resourceUri"))
        {
            JsonNode? resourceUriNode = uiMetadata["resourceUri"];
            if (resourceUriNode is not JsonValue resourceUriValue ||
                !resourceUriValue.TryGetValue(out string? existingResourceUri))
            {
                throw new ArgumentException("The tool's _meta.ui.resourceUri value must be a string.", nameof(toolOptions));
            }

            if (!string.Equals(existingResourceUri, resourceUri, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The tool's UI resource URI '{existingResourceUri}' does not match the registered resource URI '{resourceUri}'.",
                    nameof(toolOptions));
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

        builder.Services.AddSingleton(new AppToolRegistration(tool, resource, resourceUri));
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<McpServerOptions>, AppToolOptionsValidator>());

        return builder
            .WithTools([tool])
            .WithResources([resource])
            .WithMcpApps();
    }

    private static void ValidateHtmlFactory(Delegate htmlFactory)
    {
        Type returnType = htmlFactory.Method.ReturnType;
        if (returnType != typeof(string) &&
            returnType != typeof(Task<string>) &&
            returnType != typeof(ValueTask<string>))
        {
            throw new ArgumentException(
                "The HTML factory must return string, Task<string>, or ValueTask<string>.",
                nameof(htmlFactory));
        }

        var parameters = htmlFactory.Method.GetParameters();
        if (parameters.Length > 1 ||
            (parameters.Length == 1 && parameters[0].ParameterType != typeof(CancellationToken)))
        {
            throw new ArgumentException(
                "The HTML factory must have no parameters or a single CancellationToken parameter.",
                nameof(htmlFactory));
        }
    }

    private sealed class AppToolRegistration(
        McpServerTool tool,
        McpServerResource resource,
        string resourceUri)
    {
        public McpServerTool Tool { get; } = tool;

        public McpServerResource Resource { get; } = resource;

        public string ResourceUri { get; } = resourceUri;
    }

    private sealed class AppToolOptionsValidator(
        IEnumerable<AppToolRegistration> registrations,
        IEnumerable<McpServerTool> registeredTools,
        IEnumerable<McpServerResource> registeredResources) : IValidateOptions<McpServerOptions>
    {
        private readonly AppToolRegistration[] _registrations = registrations.ToArray();
        private readonly McpServerTool[] _registeredTools = registeredTools.ToArray();
        private readonly McpServerResource[] _registeredResources = registeredResources.ToArray();

        public ValidateOptionsResult Validate(string? name, McpServerOptions options)
        {
            if (!string.IsNullOrEmpty(name))
            {
                return ValidateOptionsResult.Skip;
            }

            foreach (AppToolRegistration registration in _registrations)
            {
                string toolName = registration.Tool.ProtocolTool.Name;
                if (_registeredTools.Count(tool => string.Equals(
                    tool.ProtocolTool.Name,
                    toolName,
                    StringComparison.Ordinal)) > 1)
                {
                    return ValidateOptionsResult.Fail(
                        $"The app tool name '{toolName}' is already registered. App tool names must be unique.");
                }

                AppToolRegistration? equivalentRegistration = _registrations.FirstOrDefault(candidate =>
                    !ReferenceEquals(candidate, registration) &&
                    !string.Equals(candidate.ResourceUri, registration.ResourceUri, StringComparison.Ordinal) &&
                    ResourceUrisEqual(candidate.ResourceUri, registration.ResourceUri));
                if (equivalentRegistration is not null)
                {
                    return ValidateOptionsResult.Fail(
                        $"The app resource URIs '{registration.ResourceUri}' and '{equivalentRegistration.ResourceUri}' " +
                        "identify the same resource but use different spellings. Use one exact URI for all linked tools.");
                }

                foreach (McpServerResource registeredResource in _registeredResources)
                {
                    if (ResourceUrisEqual(
                            registeredResource.ProtocolResourceTemplate?.UriTemplate,
                            registration.ResourceUri) &&
                        !_registrations.Any(candidate => ReferenceEquals(candidate.Resource, registeredResource)))
                    {
                        return ValidateOptionsResult.Fail(
                            $"The app resource URI '{registration.ResourceUri}' is already registered by a lower-level resource. " +
                            "Each app resource URI must be owned by WithAppTool.");
                    }
                }

                if (options.ToolCollection is null ||
                    !options.ToolCollection.TryGetPrimitive(toolName, out McpServerTool? selectedTool) ||
                    !ReferenceEquals(selectedTool, registration.Tool))
                {
                    return ValidateOptionsResult.Fail(
                        $"The app tool name '{toolName}' is already registered and does not resolve to this WithAppTool registration.");
                }

                if (selectedTool.ProtocolTool.Meta?["ui"] is not JsonObject uiMetadata ||
                    uiMetadata["resourceUri"] is not JsonValue resourceUriValue ||
                    !resourceUriValue.TryGetValue(out string? selectedResourceUri) ||
                    !string.Equals(selectedResourceUri, registration.ResourceUri, StringComparison.Ordinal))
                {
                    return ValidateOptionsResult.Fail(
                        $"The app tool '{toolName}' must link to the registered resource URI '{registration.ResourceUri}'.");
                }

                if (options.ResourceCollection is null ||
                    !options.ResourceCollection.TryGetPrimitive(
                        registration.ResourceUri,
                        out McpServerResource? selectedResource) ||
                    selectedResource?.ProtocolResourceTemplate is not { } resourceTemplate ||
                    !_registrations.Any(candidate =>
                        string.Equals(candidate.ResourceUri, registration.ResourceUri, StringComparison.Ordinal) &&
                        ReferenceEquals(candidate.Resource, selectedResource)) ||
                    !string.Equals(
                        resourceTemplate.UriTemplate,
                        registration.ResourceUri,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        resourceTemplate.MimeType,
                        McpApps.HtmlMimeType,
                        StringComparison.Ordinal))
                {
                    return ValidateOptionsResult.Fail(
                        $"The app tool '{toolName}' does not resolve to its HTML resource '{registration.ResourceUri}'.");
                }
            }

            return ValidateOptionsResult.Success;
        }

        private static bool ResourceUrisEqual(string? first, string? second)
        {
            if (first is not null &&
                second is not null &&
                !first.Contains('{') &&
                !second.Contains('{') &&
                Uri.TryCreate(first, UriKind.Absolute, out Uri? firstUri) &&
                Uri.TryCreate(second, UriKind.Absolute, out Uri? secondUri))
            {
                return firstUri == secondUri;
            }

            return string.Equals(first, second, StringComparison.Ordinal);
        }
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
