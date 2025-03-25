using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Reflection;

namespace ModelContextProtocol;

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
    /// <param name="services">
    /// Optional services used in the construction of the <see cref="McpServerTool"/>. These services will be
    /// used to determine which parameters should be satisifed from dependency injection, and so what services
    /// are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </param>
    /// <returns>The created <see cref="McpServerTool"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(Delegate method, IServiceProvider? services = null) =>
        AIFunctionMcpServerTool.Create(method, services);

    /// <summary>
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerTool"/>.</param>
    /// <param name="target">The instance if <paramref name="method"/> is an instance method; otherwise, <see langword="null"/>.</param>
    /// <param name="services">
    /// Optional services used in the construction of the <see cref="McpServerTool"/>. These services will be
    /// used to determine which parameters should be satisifed from dependency injection, and so what services
    /// are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </param>
    /// <returns>The created <see cref="McpServerTool"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> is an instance method but <paramref name="target"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(MethodInfo method, object? target = null, IServiceProvider? services = null) =>
        AIFunctionMcpServerTool.Create(method, target, services);

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
