using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Indicates that a method or property should be considered an <see cref="McpServerResource"/>.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is applied to methods or properties that should be exposed as resources in the Model Context Protocol. When a class
/// containing methods marked with this attribute is registered with McpServerBuilderExtensions,
/// these methods or properties become available as resources that can be called by MCP clients.
/// </para>
/// <para>
/// When methods are provided directly to <see cref="M:McpServerResource.Create"/>, the attribute is not required.
/// </para>
/// <para>
/// Read resource requests do not contain separate arguments, only a URI. However, for templated resources, portions of that URI may be considered
/// as arguments and may be bound to parameters. Further, resource methods may accept parameters that will be bound to arguments based on their type.
/// <list type="bullet">
///   <item>
///     <description>
///       <see cref="CancellationToken"/> parameters are automatically bound to a <see cref="CancellationToken"/> provided by the
///       <see cref="McpServer"/> and that respects any <see cref="CancelledNotificationParams"/>s sent by the client for this operation's
///       <see cref="RequestId"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="IServiceProvider"/> parameters are bound from the <see cref="RequestContext{ReadResourceRequestParams}"/> for this request.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="McpServer"/> parameters are bound directly to the <see cref="McpServer"/> instance associated
///       with this request's <see cref="RequestContext{ReadResourceRequestParams}"/>. Such parameters may be used to understand
///       what server is being used to process the request, and to interact with the client issuing the request to that server.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="IProgress{ProgressNotificationValue}"/> parameters accepting <see cref="ProgressNotificationValue"/> values
///       are bound to an <see cref="IProgress{ProgressNotificationValue}"/> instance manufactured to forward progress notifications
///       from the resource to the client. If the client included a <see cref="ProgressToken"/> in their request, progress reports issued
///       to this instance will propagate to the client as <see cref="NotificationMethods.ProgressNotification"/> notifications with
///       that token. If the client did not include a <see cref="ProgressToken"/>, the instance will ignore any progress reports issued to it.
///     </description>
///   </item>
///   <item>
///     <description>
///       When the <see cref="McpServerResource"/> is constructed, it may be passed an <see cref="IServiceProvider"/> via
///       <see cref="McpServerResourceCreateOptions.Services"/>. Any parameter that can be satisfied by that <see cref="IServiceProvider"/>
///       according to <see cref="IServiceProviderIsService"/> will be resolved from the <see cref="IServiceProvider"/> provided to the
///       resource invocation rather than from the argument collection.
///     </description>
///   </item>
///   <item>
///     <description>
///       Any parameter attributed with <see cref="FromKeyedServicesAttribute"/> will similarly be resolved from the
///       <see cref="IServiceProvider"/> provided to the resource invocation rather than from the argument collection.
///     </description>
///   </item>
///   <item>
///     <description>
///       All other parameters are bound from the data in the URI.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// Return values from a method are used to create the <see cref="ReadResourceResult"/> that is sent back to the client:
/// </para>
/// <list type="table">
///   <item>
///     <term><see cref="ResourceContents"/></term>
///     <description>Wrapped in a list containing the single <see cref="ResourceContents"/>.</description>
///   </item>
///   <item>
///     <term><see cref="TextContentBlock"/></term>
///     <description>Converted to a list containing a single <see cref="TextResourceContents"/>.</description>
///   </item>
///   <item>
///     <term><see cref="DataContent"/></term>
///     <description>Converted to a list containing a single <see cref="BlobResourceContents"/>.</description>
///   </item>
///   <item>
///     <term><see cref="string"/></term>
///     <description>Converted to a list containing a single <see cref="TextResourceContents"/>.</description>
///   </item>
///   <item>
///     <term><see cref="IEnumerable{ResourceContents}"/> of <see cref="ResourceContents"/></term>
///     <description>Returned directly as a list of <see cref="ResourceContents"/>.</description>
///   </item>
///   <item>
///     <term><see cref="IEnumerable{AIContent}"/> of <see cref="AIContent"/></term>
///     <description>Converted to a list containing a <see cref="TextResourceContents"/> for each <see cref="TextContentBlock"/> and a <see cref="BlobResourceContents"/> for each <see cref="DataContent"/>.</description>
///   </item>
///   <item>
///     <term><see cref="IEnumerable{String}"/> of <see cref="string"/></term>
///     <description>Converted to a list containing a <see cref="TextResourceContents"/>, one for each <see cref="string"/>.</description>
///   </item>
/// </list>
/// <para>
/// Other returned types will result in an <see cref="InvalidOperationException"/> being thrown.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpServerResourceAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerResourceAttribute"/> class.
    /// </summary>
    public McpServerResourceAttribute()
    {
    }

    /// <summary>Gets or sets the URI template of the resource.</summary>
    /// <remarks>
    /// If <see langword="null"/>, a URI will be derived from <see cref="Name"/> and the method's parameter names.
    /// This template can, but doesn't have to, include parameters; if it does, this <see cref="McpServerResource"/>
    /// is considered a "resource template", and if it doesn't, it is considered a "direct resource".
    /// The former will be listed with <see cref="RequestMethods.ResourcesTemplatesList"/> requests and the latter
    /// with <see cref="RequestMethods.ResourcesList"/> requests.
    /// </remarks>
    public string? UriTemplate { get; set; }

    /// <summary>Gets or sets the name of the resource.</summary>
    /// <remarks>If <see langword="null"/>, the method name is used.</remarks>
    public string? Name { get; set; }

    /// <summary>Gets or sets the title of the resource.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the MIME (media) type of the resource.</summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the source URI for the resource's icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This can be an HTTP/HTTPS URL pointing to an image file or a data URI with base64-encoded image data.
    /// When specified, a single icon will be added to the resource.
    /// </para>
    /// <para>
    /// For more advanced icon configuration (multiple icons, MIME type specification, size characteristics),
    /// use <see cref="McpServerResourceCreateOptions.Icons"/> when creating the resource programmatically.
    /// </para>
    /// </remarks>
    public string? IconSource { get; set; }
}
