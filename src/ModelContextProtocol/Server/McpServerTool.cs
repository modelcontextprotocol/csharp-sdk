using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ModelContextProtocol.Server;

/// <summary>Represents an invocable tool used by Model Context Protocol clients and servers.</summary>
public abstract class McpServerTool
{
    /// <summary>Initializes a new instance of the <see cref="McpServerTool"/> class.</summary>
    protected McpServerTool()
    {
    }

    /// <summary>Gets the protocol <see cref="Tool"/> type for this instance.</summary>
    public abstract Tool ProtocolTool { get; }

    /// <summary>Invokes the <see cref="McpServerTool"/>.</summary>
    /// <param name="request">The request information resulting in the invocation of this tool.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The call response from invoking the tool.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public abstract Task<CallToolResponse> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerTool"/>.</param>
    /// <param name="name">
    /// The name to use for the <see cref="McpServerTool"/>. If <see langword="null"/>, but an <see cref="McpServerToolAttribute"/>
    /// is applied to <paramref name="method"/>, the name from the attribute will be used. If that's not present, the name based
    /// on <paramref name="method"/>'s name will be used.
    /// </param>
    /// <param name="description">
    /// The description to use for the <see cref="McpServerTool"/>. If <see langword="null"/>, but a <see cref="DescriptionAttribute"/>
    /// is applied to <paramref name="method"/>, the description from that attribute will be used.
    /// </param>
    /// <param name="services">
    /// Optional services used in the construction of the <see cref="McpServerTool"/>. These services will be
    /// used to determine which parameters should be satisifed from dependency injection; what services
    /// are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </param>
    /// <returns>The created <see cref="McpServerTool"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(
        Delegate method,
        string? name = null, 
        string? description = null, 
        IServiceProvider? services = null) =>
        AIFunctionMcpServerTool.Create(method, name, description, services);

    /// <summary>
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerTool"/>.</param>
    /// <param name="target">The instance if <paramref name="method"/> is an instance method; otherwise, <see langword="null"/>.</param>
    /// <param name="name">
    /// The name to use for the <see cref="McpServerTool"/>. If <see langword="null"/>, but an <see cref="McpServerToolAttribute"/>
    /// is applied to <paramref name="method"/>, the name from the attribute will be used. If that's not present, the name based
    /// on <paramref name="method"/>'s name will be used.
    /// </param>
    /// <param name="description">
    /// The description to use for the <see cref="McpServerTool"/>. If <see langword="null"/>, but a <see cref="DescriptionAttribute"/>
    /// is applied to <paramref name="method"/>, the description from that attribute will be used.
    /// </param>
    /// <param name="services">
    /// Optional services used in the construction of the <see cref="McpServerTool"/>. These services will be
    /// used to determine which parameters should be satisifed from dependency injection; what services
    /// are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </param>
    /// <returns>The created <see cref="McpServerTool"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> is an instance method but <paramref name="target"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(
        MethodInfo method, 
        object? target = null,
        string? name = null,
        string? description = null,
        IServiceProvider? services = null) =>
        AIFunctionMcpServerTool.Create(method, target, name, description, services);

    /// <summary>
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via an <see cref="MethodInfo"/> for
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
    /// <param name="name">
    /// The name to use for the <see cref="McpServerTool"/>. If <see langword="null"/>, but an <see cref="McpServerToolAttribute"/>
    /// is applied to <paramref name="method"/>, the name from the attribute will be used. If that's not present, the name based
    /// on <paramref name="method"/>'s name will be used.
    /// </param>
    /// <param name="description">
    /// The description to use for the <see cref="McpServerTool"/>. If <see langword="null"/>, but a <see cref="DescriptionAttribute"/>
    /// is applied to <paramref name="method"/>, the description from that attribute will be used.
    /// </param>
    /// <param name="services">
    /// Optional services used in the construction of the <see cref="McpServerTool"/>. These services will be
    /// used to determine which parameters should be satisifed from dependency injection; what services
    /// are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </param>
    /// <returns>The created <see cref="AIFunction"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(
        MethodInfo method,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type targetType,
        string? name = null,
        string? description = null,
        IServiceProvider? services = null) =>
        AIFunctionMcpServerTool.Create(method, targetType, name, description, services);

    /// <summary>Creates an <see cref="McpServerTool"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    /// <param name="function">The function to wrap.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Unlike the other overloads of Create, the <see cref="McpServerTool"/> created by <see cref="Create(AIFunction)"/>
    /// does not provide all of the special parameter handling for MCP-specific concepts, like <see cref="IMcpServer"/>.
    /// </remarks>
    public static McpServerTool Create(AIFunction function) =>
        AIFunctionMcpServerTool.Create(function);

    /// <inheritdoc />
    public override string ToString() => ProtocolTool.Name;
}
