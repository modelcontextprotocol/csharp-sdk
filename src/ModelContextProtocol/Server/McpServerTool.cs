using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an invocable tool used by Model Context Protocol clients and servers.
/// </summary>
/// <remarks>
/// <para>
/// In the Model Context Protocol (MCP), tools are functions that can be invoked by clients, typically 
/// AI models or agents. They provide a way to extend model capabilities by allowing access to 
/// external functionality, data sources, or services.
/// </para>
/// <para>
/// Tools can be created in several ways:
/// <list type="bullet">
///   <item>Using the static <see cref="M:Create"/> methods with delegates or method references</item>
///   <item>By creating classes marked with <see cref="McpServerToolTypeAttribute"/> containing methods marked with <see cref="McpServerToolAttribute"/></item>
///   <item>By inheriting from <see cref="McpServerTool"/> to create custom tool implementations</item>
/// </list>
/// </para>
/// <para>
/// Example usage - creating and registering tools:
/// <code>
/// // Create a tool directly with a delegate
/// var echoTool = McpServerTool.Create(
///     (string message) => $"Echo: {message}",
///     new McpServerToolCreateOptions
///     {
///         Name = "echo",
///         Description = "Echoes the input back to the client."
///     });
///     
/// // Register tools with the server
/// builder.Services.AddMcpServer()
///     .WithStdioServerTransport()
///     .WithTool(echoTool)
///     .WithTools&lt;WeatherTools&gt;(); // Register a class containing tool methods
/// </code>
/// </para>
/// <para>
/// Example using a class with tool methods:
/// <code>
/// [McpServerToolType]
/// public class WeatherTools
/// {
///     [McpServerTool, Description("Get weather forecast for a location.")]
///     public static async Task&lt;string&gt; GetForecast(
///         HttpClient client,
///         [Description("Latitude of the location.")] double latitude,
///         [Description("Longitude of the location.")] double longitude)
///     {
///         // Implementation details...
///         return forecast;
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public abstract class McpServerTool : IMcpServerPrimitive
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
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerTool"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerTool"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(
        Delegate method,
        McpServerToolCreateOptions? options = null) =>
        AIFunctionMcpServerTool.Create(method, options);

    /// <summary>
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    /// <param name="method">The method to be represented via the created <see cref="McpServerTool"/>.</param>
    /// <param name="target">The instance if <paramref name="method"/> is an instance method; otherwise, <see langword="null"/>.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerTool"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerTool"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> is an instance method but <paramref name="target"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(
        MethodInfo method, 
        object? target = null,
        McpServerToolCreateOptions? options = null) =>
        AIFunctionMcpServerTool.Create(method, target, options);

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
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerTool"/> to control its behavior.</param>
    /// <returns>The created <see cref="AIFunction"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(
        MethodInfo method,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type targetType,
        McpServerToolCreateOptions? options = null) =>
        AIFunctionMcpServerTool.Create(method, targetType, options);

    /// <summary>Creates an <see cref="McpServerTool"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    /// <param name="function">The function to wrap.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerTool"/> to control its behavior.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Unlike the other overloads of Create, the <see cref="McpServerTool"/> created by <see cref="Create(AIFunction, McpServerToolCreateOptions)"/>
    /// does not provide all of the special parameter handling for MCP-specific concepts, like <see cref="IMcpServer"/>.
    /// </remarks>
    public static McpServerTool Create(
        AIFunction function,
        McpServerToolCreateOptions? options = null) =>
        AIFunctionMcpServerTool.Create(function, options);

    /// <inheritdoc />
    public override string ToString() => ProtocolTool.Name;

    /// <inheritdoc />
    string IMcpServerPrimitive.Name => ProtocolTool.Name;
}
