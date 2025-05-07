using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an invocable resource used by Model Context Protocol clients and servers.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="McpServerResource"/> is an abstract base class that represents an MCP resource template for use in the server (as opposed
/// to <see cref="ResourceTemplate"/>, which provides the protocol representation of a resource. Instances of 
/// <see cref="McpServerResourceTemplate"/> can be added into a <see cref="IServiceCollection"/> to be picked up automatically when
/// <see cref="McpServerFactory"/> is used to create an <see cref="IMcpServer"/>, or added into a <see cref="McpServerPrimitiveCollection{McpServerResourceTemplate}"/>.
/// </para>
/// <para>
/// Most commonly, <see cref="McpServerResourceTemplate"/> instances are created using the static <see cref="M:McpServerResourceTemplate.Create"/> methods.
/// These methods enable creating an <see cref="McpServerResourceTemplate"/> for a method, specified via a <see cref="Delegate"/> or 
/// <see cref="MethodInfo"/>, and are what are used implicitly by <see cref="McpServerBuilderExtensions.WithResourcesFromAssembly"/> and
/// <see cref="M:McpServerBuilderExtensions.WithResources"/>. The <see cref="M:McpServerResourceTemplate.Create"/> methods
/// create <see cref="McpServerResourceTemplate"/> instances capable of working with a large variety of .NET method signatures, automatically handling
/// how parameters are marshaled into the method from the URI received from the MCP client, and how the return value is marshaled back
/// into the <see cref="ReadResourceResult"/> that's then serialized and sent back to the client.
/// </para>
/// <para>
/// Resource read requests do not contain arguments, only a URI. However, resource methods may accept parameters that will be bound
/// to arguments based on their type.
/// <list type="bullet">
///   <item>
///     <description>
///       <see cref="CancellationToken"/> parameters are automatically bound to a <see cref="CancellationToken"/> provided by the
///       <see cref="IMcpServer"/> and that respects any <see cref="CancelledNotification"/>s sent by the client for this operation's
///       <see cref="RequestId"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="IServiceProvider"/> parameters are bound from the <see cref="RequestContext{GetResourceRequestParams}"/> for this request.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="IMcpServer"/> parameters are bound directly to the <see cref="IMcpServer"/> instance associated
///       with this request's <see cref="RequestContext{CallResourceRequestParams}"/>. Such parameters may be used to understand
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
/// </list>
/// </para>
/// <para>
/// Further, the URI itself contains arguments formatted into the URI template. These arguments are bound to <see cref="string"/>
/// parameters of the corresponding name in the method signature.
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
///     <description>Converted to a list containing a <see cref="TextResourceContents"/> for each <see cref="TextContent"/> and a <see cref="BlobResourceContents"/> for each <see cref="DataContent"/>.</description>
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
public abstract class McpServerResourceTemplate : IMcpServerPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="McpServerResourceTemplate"/> class.</summary>
    protected McpServerResourceTemplate()
    {
    }

    /// <summary>Gets the protocol <see cref="ResourceTemplate"/> type for this instance.</summary>
    /// <remarks>
    /// The ProtocolResourceTemplate property represents the underlying resource template definition as defined in the
    /// Model Context Protocol specification. It contains metadata like the resource templates's URI template, name, and description.
    /// </remarks>
    public abstract ResourceTemplate ProtocolResourceTemplate { get; }

    /// <summary>
    /// Gets the resource template, rendering it with the provided request parameters and returning the resource result.
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
    /// the resource content and messages. If and only if this <see cref="McpServerResourceTemplate"/> doesn't match the <see cref="ReadResourceRequestParams.Uri"/>,
    /// the method returns <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The resource implementation returns <see langword="null"/> or an unsupported result type.</exception>
    public abstract ValueTask<ReadResourceResult?> ReadAsync(
        RequestContext<ReadResourceRequestParams> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an <see cref="McpServerResourceTemplate"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerResource"/>.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerResource"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerResource"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerResourceTemplate Create(
        Delegate method,
        McpServerResourceTemplateCreateOptions? options = null) =>
        AIFunctionMcpServerResourceTemplate.Create(method, options);

    /// <summary>
    /// Creates an <see cref="McpServerResourceTemplate"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerResource"/>.</param>
    /// <param name="target">The instance if <paramref name="method"/> is an instance method; otherwise, <see langword="null"/>.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerResource"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerResource"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> is an instance method but <paramref name="target"/> is <see langword="null"/>.</exception>
    public static McpServerResourceTemplate Create(
        MethodInfo method, 
        object? target = null,
        McpServerResourceTemplateCreateOptions? options = null) =>
        AIFunctionMcpServerResourceTemplate.Create(method, target, options);

    /// <summary>
    /// Creates an <see cref="McpServerResourceTemplate"/> instance for a method, specified via an <see cref="MethodInfo"/> for
    /// and instance method, along with a <see cref="Type"/> representing the type of the target object to
    /// instantiate each time the method is invoked.
    /// </summary>
    /// <param name="method">The instance method to be represented via the created <see cref="AIFunction"/>.</param>
    /// <param name="targetType">
    /// The <see cref="Type"/> to construct an instance of on which to invoke <paramref name="method"/> when
    /// the resulting <see cref="AIFunction"/> is invoked. If services are provided,
    /// ActivatorUtilities.CreateInstance will be used to construct the instance using those services; otherwise,
    /// <see cref="Activator.CreateInstance(Type)"/> is used, utilizing the type's public parameterless constructor.
    /// If an instance can't be constructed, an exception is thrown during the function's invocation.
    /// </param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerResource"/> to control its behavior.</param>
    /// <returns>The created <see cref="AIFunction"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerResourceTemplate Create(
        MethodInfo method,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type targetType,
        McpServerResourceTemplateCreateOptions? options = null) =>
        AIFunctionMcpServerResourceTemplate.Create(method, targetType, options);

    /// <summary>Creates an <see cref="McpServerResourceTemplate"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    /// <param name="function">The function to wrap.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerResource"/> to control its behavior.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Unlike the other overloads of Create, the <see cref="McpServerResource"/> created by <see cref="Create(AIFunction, McpServerResourceTemplateCreateOptions)"/>
    /// does not provide all of the special parameter handling for MCP-specific concepts, like <see cref="IMcpServer"/>.
    /// </remarks>
    public static McpServerResourceTemplate Create(
        AIFunction function,
        McpServerResourceTemplateCreateOptions? options = null) =>
        AIFunctionMcpServerResourceTemplate.Create(function, options);

    /// <inheritdoc />
    public override string ToString() => ProtocolResourceTemplate.UriTemplate;

    /// <inheritdoc />
    string IMcpServerPrimitive.Id => ProtocolResourceTemplate.UriTemplate;
}
