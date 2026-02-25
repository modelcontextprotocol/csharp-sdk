using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Reflection;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an invocable resource used by Model Context Protocol clients and servers.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="McpServerResource"/> is an abstract base class that represents an MCP resource for use in the server (as opposed
/// to <see cref="Resource"/> or <see cref="ResourceTemplate"/>, which provide the protocol representations of a resource). Instances of
/// <see cref="McpServerResource"/> can be added into a <see cref="IServiceCollection"/> to be picked up automatically when
/// <see cref="McpServer"/> is used to create an <see cref="McpServer"/>, or added into a <see cref="McpServerPrimitiveCollection{McpServerResource}"/>.
/// </para>
/// <para>
/// Most commonly, <see cref="McpServerResource"/> instances are created using the static <see cref="M:McpServerResource.Create"/> methods.
/// These methods enable creating an <see cref="McpServerResource"/> for a method, specified via a <see cref="Delegate"/> or
/// <see cref="MethodInfo"/>, and are what are used implicitly by WithResourcesFromAssembly and
/// <see cref="M:McpServerBuilderExtensions.WithResources"/>. The <see cref="M:McpServerResource.Create"/> methods
/// create <see cref="McpServerResource"/> instances capable of working with a large variety of .NET method signatures, automatically handling
/// how parameters are marshaled into the method from the URI received from the MCP client, and how the return value is marshaled back
/// into the <see cref="ReadResourceResult"/> that's then serialized and sent back to the client.
/// </para>
/// <para>
/// <see cref="McpServerResource"/> is used to represent both direct resources (for example,"resource://example") and templated
/// resources (for example,"resource://example/{id}").
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
///     <term><see cref="TextContent"/></term>
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
///   <item>
///     <term><see cref="ReadResourceResult"/></term>
///     <description>Returned directly without modification.</description>
///   </item>
/// </list>
/// <para>
/// Other returned types will result in an <see cref="InvalidOperationException"/> being thrown.
/// </para>
/// </remarks>
public abstract class McpServerResource : IMcpServerPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="McpServerResource"/> class.</summary>
    protected McpServerResource()
    {
    }

    /// <summary>Gets a value that indicates whether this resource is a URI template with parameters as opposed to a direct resource.</summary>
    public bool IsTemplated => ProtocolResourceTemplate.UriTemplate.Contains('{');

    /// <summary>Gets the protocol <see cref="ResourceTemplate"/> type for this instance.</summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ProtocolResourceTemplate"/> property represents the underlying resource template definition as defined in the
    /// Model Context Protocol specification. It contains metadata like the resource template's URI template, name, and description.
    /// </para>
    /// <para>
    /// Every valid resource URI is a valid resource URI template, and thus this property always returns an instance.
    /// In contrast, the <see cref="ProtocolResource"/> property might return <see langword="null"/> if the resource template
    /// contains a parameter, in which case the resource template URI is not a valid resource URI.
    /// </para>
    /// </remarks>
    public abstract ResourceTemplate ProtocolResourceTemplate { get; }

    /// <summary>Gets the protocol <see cref="Resource"/> type for this instance.</summary>
    /// <remarks>
    /// The ProtocolResource property represents the underlying resource definition as defined in the
    /// Model Context Protocol specification. It contains metadata like the resource template's URI template, name, and description.
    /// </remarks>
    public virtual Resource? ProtocolResource => ProtocolResourceTemplate.AsResource();

    /// <summary>
    /// Gets the metadata for this resource instance.
    /// </summary>
    /// <remarks>
    /// Contains attributes from the associated MethodInfo and declaring class (if any),
    /// with class-level attributes appearing before method-level attributes.
    /// </remarks>
    public abstract IReadOnlyList<object> Metadata { get; }

    /// <summary>
    /// Evaluates whether the <paramref name="uri"/> matches the <see cref="ProtocolResourceTemplate"/>
    /// and can be used as the <see cref="ReadResourceRequestParams.Uri"/> passed to <see cref="ReadAsync"/>.
    /// </summary>
    /// <param name="uri">The URI being evaluated for this resource.</param>
    /// <returns>
    /// <see langword="true"/> if the <paramref name="uri"/> matches the <see cref="ProtocolResourceTemplate"/>; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public abstract bool IsMatch(string uri);

    /// <summary>
    /// Gets the resource, rendering it with the provided request parameters and returning the resource result.
    /// </summary>
    /// <param name="request">
    /// The request context containing information about the resource invocation, including any arguments
    /// passed to the resource. This object provides access to both the request parameters and the server context.
    /// </param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{ReadResourceResult}"/> representing the asynchronous operation, containing a <see cref="ReadResourceResult"/> with
    /// the resource content and messages.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The <see cref="ReadResourceRequestParams.Uri"/> did not match the <see cref="ProtocolResourceTemplate"/> for this resource,
    /// the resource implementation returned <see langword="null"/>, or the resource implementation returned an unsupported result type.
    /// </exception>
    public abstract ValueTask<ReadResourceResult> ReadAsync(
        RequestContext<ReadResourceRequestParams> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an <see cref="McpServerResource"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerResource"/>.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerResource"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerResource"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerResource Create(
        Delegate method,
        McpServerResourceCreateOptions? options = null) =>
        AIFunctionMcpServerResource.Create(method, options);

    /// <summary>
    /// Creates an <see cref="McpServerResource"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerResource"/>.</param>
    /// <param name="target">The instance if <paramref name="method"/> is an instance method; otherwise, <see langword="null"/>.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerResource"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerResource"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> is an instance method but <paramref name="target"/> is <see langword="null"/>.</exception>
    public static McpServerResource Create(
        MethodInfo method,
        object? target = null,
        McpServerResourceCreateOptions? options = null) =>
        AIFunctionMcpServerResource.Create(method, target, options);

    /// <summary>
    /// Creates an <see cref="McpServerResource"/> instance for a method, specified via a <see cref="MethodInfo"/> for
    /// an instance method, along with a <see cref="Type"/> representing the type of the target object to
    /// instantiate each time the method is invoked.
    /// </summary>
    /// <param name="method">The instance method to be represented via the created <see cref="McpServerResource"/>.</param>
    /// <param name="createTargetFunc">
    /// The callback used on each function invocation to create an instance of the type on which the instance method <paramref name="method"/>
    /// will be invoked. If the returned instance is <see cref="IAsyncDisposable"/> or <see cref="IDisposable"/>, it will
    /// be disposed of after method completes its invocation.
    /// </param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerResource"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerResource"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> or <paramref name="createTargetFunc"/> is <see langword="null"/>.</exception>
    public static McpServerResource Create(
        MethodInfo method,
        Func<RequestContext<ReadResourceRequestParams>, object> createTargetFunc,
        McpServerResourceCreateOptions? options = null) =>
        AIFunctionMcpServerResource.Create(method, createTargetFunc, options);

    /// <summary>Creates an <see cref="McpServerResource"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    /// <param name="function">The function to wrap.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerResource"/> to control its behavior.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Unlike the other overloads of Create, the <see cref="McpServerResource"/> created by <see cref="Create(AIFunction, McpServerResourceCreateOptions)"/>
    /// does not provide all of the special parameter handling for MCP-specific concepts, like <see cref="McpServer"/>.
    /// </remarks>
    public static McpServerResource Create(
        AIFunction function,
        McpServerResourceCreateOptions? options = null) =>
        AIFunctionMcpServerResource.Create(function, options);

    /// <inheritdoc />
    public override string ToString() => ProtocolResourceTemplate.UriTemplate;

    /// <inheritdoc />
    string IMcpServerPrimitive.Id => ProtocolResourceTemplate.UriTemplate;
}
