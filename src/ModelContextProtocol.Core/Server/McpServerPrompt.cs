using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Reflection;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an invocable prompt used by Model Context Protocol clients and servers.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="McpServerPrompt"/> is an abstract base class that represents an MCP prompt for use in the server (as opposed
/// to <see cref="Prompt"/>, which provides the protocol representation of a prompt, and <see cref="McpClientPrompt"/>, which
/// provides a client-side representation of a prompt). Instances of <see cref="McpServerPrompt"/> can be added into a
/// <see cref="IServiceCollection"/> to be picked up automatically when <see cref="McpServer"/> is used to create
/// an <see cref="McpServer"/>, or added into a <see cref="McpServerPrimitiveCollection{McpServerPrompt}"/>.
/// </para>
/// <para>
/// Most commonly, <see cref="McpServerPrompt"/> instances are created using the static <see cref="M:McpServerPrompt.Create"/> methods.
/// These methods enable creating an <see cref="McpServerPrompt"/> for a method, specified via a <see cref="Delegate"/> or
/// <see cref="MethodInfo"/>, and are what are used implicitly by WithPromptsFromAssembly and WithPrompts. The <see cref="M:McpServerPrompt.Create"/> methods
/// create <see cref="McpServerPrompt"/> instances capable of working with a large variety of .NET method signatures, automatically handling
/// how parameters are marshaled into the method from the JSON received from the MCP client, and how the return value is marshaled back
/// into the <see cref="GetPromptResult"/> that's then serialized and sent back to the client.
/// </para>
/// <para>
/// By default, parameters are sourced from the <see cref="GetPromptRequestParams.Arguments"/> dictionary, which is a collection
/// of key/value pairs. Those parameters are deserialized from the
/// <see cref="JsonElement"/> values in that collection. There are a few exceptions to this:
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
///       <see cref="IServiceProvider"/> parameters are bound from the <see cref="RequestContext{GetPromptRequestParams}"/> for this request.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="McpServer"/> parameters are bound directly to the <see cref="McpServer"/> instance associated
///       with this request's <see cref="RequestContext{GetPromptRequestParams}"/>. Such parameters may be used to understand
///       what server is being used to process the request, and to interact with the client issuing the request to that server.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="IProgress{ProgressNotificationValue}"/> parameters accepting <see cref="ProgressNotificationValue"/> values
///       are bound to an <see cref="IProgress{ProgressNotificationValue}"/> instance manufactured to forward progress notifications
///       from the prompt to the client. If the client included a <see cref="ProgressToken"/> in their request, progress reports issued
///       to this instance will propagate to the client as <see cref="NotificationMethods.ProgressNotification"/> notifications with
///       that token. If the client did not include a <see cref="ProgressToken"/>, the instance will ignore any progress reports issued to it.
///     </description>
///   </item>
///   <item>
///     <description>
///       When the <see cref="McpServerPrompt"/> is constructed, it may be passed an <see cref="IServiceProvider"/> via
///       <see cref="McpServerPromptCreateOptions.Services"/>. Any parameter that can be satisfied by that <see cref="IServiceProvider"/>
///       according to <see cref="IServiceProviderIsService"/> will be resolved from the <see cref="IServiceProvider"/> provided to
///       <see cref="GetAsync"/> rather than from the argument collection.
///     </description>
///   </item>
///   <item>
///     <description>
///       Any parameter attributed with <see cref="FromKeyedServicesAttribute"/> will similarly be resolved from the
///       <see cref="IServiceProvider"/> provided to <see cref="GetAsync"/> rather than from the argument collection.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// All other parameters are deserialized from the <see cref="JsonElement"/>s in the <see cref="GetPromptRequestParams.Arguments"/> dictionary.
/// </para>
/// <para>
/// In general, the data supplied via the <see cref="GetPromptRequestParams.Arguments"/>'s dictionary is passed along from the caller and
/// should thus be considered unvalidated and untrusted. To provide validated and trusted data to the invocation of the prompt, consider having
/// the prompt be an instance method, referring to data stored in the instance, or using an instance or parameters resolved from the <see cref="IServiceProvider"/>
/// to provide data to the method.
/// </para>
/// <para>
/// Return values from a method are used to create the <see cref="GetPromptResult"/> that is sent back to the client:
/// </para>
/// <list type="table">
///   <item>
///     <term><see cref="string"/></term>
///     <description>Converted to a list containing a single <see cref="PromptMessage"/> with its <see cref="PromptMessage.Content"/> set to contain the <see cref="string"/>.</description>
///   </item>
///   <item>
///     <term><see cref="PromptMessage"/></term>
///     <description>Converted to a list containing the single <see cref="PromptMessage"/>.</description>
///   </item>
///   <item>
///     <term><see cref="IEnumerable{PromptMessage}"/> of <see cref="PromptMessage"/></term>
///     <description>Converted to a list containing all of the returned <see cref="PromptMessage"/> instances.</description>
///   </item>
///   <item>
///     <term><see cref="ChatMessage"/></term>
///     <description>Converted to a list of <see cref="PromptMessage"/> instances derived from the <see cref="ChatMessage"/> with <see cref="AIContentExtensions.ToPromptMessages"/>.</description>
///   </item>
///   <item>
///     <term><see cref="IEnumerable{ChatMessage}"/> of <see cref="ChatMessage"/></term>
///     <description>Converted to a list of <see cref="PromptMessage"/> instances derived from all of the <see cref="ChatMessage"/> instances with <see cref="AIContentExtensions.ToPromptMessages"/>.</description>
///   </item>
///   <item>
///     <term><see cref="GetPromptResult"/></term>
///     <description>Returned directly without modification.</description>
///   </item>
/// </list>
/// <para>
/// Other returned types will result in an <see cref="InvalidOperationException"/> being thrown.
/// </para>
/// </remarks>
public abstract class McpServerPrompt : IMcpServerPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="McpServerPrompt"/> class.</summary>
    protected McpServerPrompt()
    {
    }

    /// <summary>Gets the protocol <see cref="Prompt"/> type for this instance.</summary>
    /// <remarks>
    /// The ProtocolPrompt property represents the underlying prompt definition as defined in the
    /// Model Context Protocol specification. It contains metadata like the prompt's name,
    /// description, and acceptable arguments.
    /// </remarks>
    public abstract Prompt ProtocolPrompt { get; }

    /// <summary>
    /// Gets the metadata for this prompt instance.
    /// </summary>
    /// <remarks>
    /// Contains attributes from the associated MethodInfo and declaring class (if any),
    /// with class-level attributes appearing before method-level attributes.
    /// </remarks>
    public abstract IReadOnlyList<object> Metadata { get; }

    /// <summary>
    /// Gets the prompt, rendering it with the provided request parameters and returning the prompt result.
    /// </summary>
    /// <param name="request">
    /// The request context containing information about the prompt invocation, including any arguments
    /// passed to the prompt. This object provides access to both the request parameters and the server context.
    /// </param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation, containing a <see cref="GetPromptResult"/> with
    /// the prompt content and messages.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The prompt implementation returns <see langword="null"/> or an unsupported result type.</exception>
    public abstract ValueTask<GetPromptResult> GetAsync(
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
    /// Creates an <see cref="McpServerPrompt"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
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
    /// Creates an <see cref="McpServerPrompt"/> instance for a method, specified via a <see cref="MethodInfo"/> for
    /// an instance method, along with a <see cref="Type"/> representing the type of the target object to
    /// instantiate each time the method is invoked.
    /// </summary>
    /// <param name="method">The instance method to be represented via the created <see cref="McpServerPrompt"/>.</param>
    /// <param name="createTargetFunc">
    /// Callback used on each function invocation to create an instance of the type on which the instance method <paramref name="method"/>
    /// will be invoked. If the returned instance is <see cref="IAsyncDisposable"/> or <see cref="IDisposable"/>, it will
    /// be disposed of after method completes its invocation.
    /// </param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerPrompt"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerPrompt"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> or <paramref name="createTargetFunc"/> is <see langword="null"/>.</exception>
    public static McpServerPrompt Create(
        MethodInfo method,
        Func<RequestContext<GetPromptRequestParams>, object> createTargetFunc,
        McpServerPromptCreateOptions? options = null) =>
        AIFunctionMcpServerPrompt.Create(method, createTargetFunc, options);

    /// <summary>Creates an <see cref="McpServerPrompt"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    /// <param name="function">The function to wrap.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerPrompt"/> to control its behavior.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Unlike the other overloads of Create, the <see cref="McpServerPrompt"/> created by <see cref="Create(AIFunction, McpServerPromptCreateOptions)"/>
    /// does not provide all of the special parameter handling for MCP-specific concepts, like <see cref="McpServer"/>.
    /// </remarks>
    public static McpServerPrompt Create(
        AIFunction function,
        McpServerPromptCreateOptions? options = null) =>
        AIFunctionMcpServerPrompt.Create(function, options);

    /// <inheritdoc />
    public override string ToString() => ProtocolPrompt.Name;

    /// <inheritdoc />
    string IMcpServerPrimitive.Id => ProtocolPrompt.Name;
}
