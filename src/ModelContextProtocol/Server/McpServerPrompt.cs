using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ModelContextProtocol.Server;

/// <summary>Represents an invocable prompt used by Model Context Protocol servers.</summary>
public abstract class McpServerPrompt : IMcpServerPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="McpServerPrompt"/> class.</summary>
    protected McpServerPrompt()
    {
    }

    /// <summary>Gets the protocol <see cref="Prompt"/> type for this instance.</summary>
    public abstract Prompt ProtocolPrompt { get; }

    /// <summary>Invokes the <see cref="McpServerPrompt"/>.</summary>
    /// <param name="request">The request information resulting in the invocation of this tool.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The call response from invoking the tool.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public abstract Task<GetPromptResult> GetAsync(
        RequestContext<GetPromptRequestParams> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an <see cref="McpServerPrompt"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerPrompt"/>.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerPrompt"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerPrompt"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerPrompt Create(
        Delegate method,
        McpServerPromptCreateOptions? options = null) =>
        AIFunctionMcpServerPrompt.Create(method, options);

    /// <summary>
    /// Creates an <see cref="McpServerPrompt"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerPrompt"/>.</param>
    /// <param name="target">The instance if <paramref name="method"/> is an instance method; otherwise, <see langword="null"/>.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerPrompt"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerPrompt"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> is an instance method but <paramref name="target"/> is <see langword="null"/>.</exception>
    public static McpServerPrompt Create(
        MethodInfo method, 
        object? target = null,
        McpServerPromptCreateOptions? options = null) =>
        AIFunctionMcpServerPrompt.Create(method, target, options);

    /// <summary>
    /// Creates an <see cref="McpServerPrompt"/> instance for a method, specified via an <see cref="MethodInfo"/> for
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
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerPrompt"/> to control its behavior.</param>
    /// <returns>The created <see cref="AIFunction"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerPrompt Create(
        MethodInfo method,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type targetType,
        McpServerPromptCreateOptions? options = null) =>
        AIFunctionMcpServerPrompt.Create(method, targetType, options);

    /// <summary>Creates an <see cref="McpServerPrompt"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    /// <param name="function">The function to wrap.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerPrompt"/> to control its behavior.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Unlike the other overloads of Create, the <see cref="McpServerPrompt"/> created by <see cref="Create(AIFunction, McpServerPromptCreateOptions)"/>
    /// does not provide all of the special parameter handling for MCP-specific concepts, like <see cref="IMcpServer"/>.
    /// </remarks>
    public static McpServerPrompt Create(
        AIFunction function,
        McpServerPromptCreateOptions? options = null) =>
        AIFunctionMcpServerPrompt.Create(function, options);

    /// <inheritdoc />
    public override string ToString() => ProtocolPrompt.Name;

    /// <inheritdoc />
    string IMcpServerPrimitive.Name => ProtocolPrompt.Name;
}
