using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Reflection;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an invocable tool used by Model Context Protocol clients and servers.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="McpServerTool"/> is an abstract base class that represents an MCP tool for use in the server (as opposed
/// to <see cref="Tool"/>, which provides the protocol representation of a tool, and <see cref="McpClientTool"/>, which
/// provides a client-side representation of a tool). Instances of <see cref="McpServerTool"/> can be added into a
/// <see cref="IServiceCollection"/> to be picked up automatically when <see cref="McpServer"/> is used to create
/// an <see cref="McpServer"/>, or added into a <see cref="McpServerPrimitiveCollection{McpServerTool}"/>.
/// </para>
/// <para>
/// Most commonly, <see cref="McpServerTool"/> instances are created using the static <see cref="M:McpServerTool.Create"/> methods.
/// These methods enable creating an <see cref="McpServerTool"/> for a method, specified via a <see cref="Delegate"/> or
/// <see cref="MethodInfo"/>, and are what are used implicitly by WithToolsFromAssembly and WithTools. The <see cref="M:McpServerTool.Create"/> methods
/// create <see cref="McpServerTool"/> instances capable of working with a large variety of .NET method signatures, automatically handling
/// how parameters are marshaled into the method from the JSON received from the MCP client, and how the return value is marshaled back
/// into the <see cref="CallToolResult"/> that's then serialized and sent back to the client.
/// </para>
/// <para>
/// By default, parameters are sourced from the <see cref="CallToolRequestParams.Arguments"/> dictionary, which is a collection
/// of key/value pairs, and are represented in the JSON schema for the function, as exposed in the returned <see cref="McpServerTool"/>'s
/// <see cref="ProtocolTool"/>'s <see cref="Tool.InputSchema"/>. Those parameters are deserialized from the
/// <see cref="JsonElement"/> values in that collection. There are a few exceptions to this:
/// <list type="bullet">
///   <item>
///     <description>
///       <see cref="CancellationToken"/> parameters are automatically bound to a <see cref="CancellationToken"/> provided by the
///       <see cref="McpServer"/> and that respects any <see cref="CancelledNotificationParams"/>s sent by the client for this operation's
///       <see cref="RequestId"/>. The parameter is not included in the generated JSON schema.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="IServiceProvider"/> parameters are bound from the <see cref="RequestContext{CallToolRequestParams}"/> for this request,
///       and are not included in the JSON schema.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="McpServer"/> parameters are not included in the JSON schema and are bound directly to the <see cref="McpServer"/>
///       instance associated with this request's <see cref="RequestContext{CallToolRequestParams}"/>. Such parameters may be used to understand
///       what server is being used to process the request, and to interact with the client issuing the request to that server.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="IProgress{ProgressNotificationValue}"/> parameters accepting <see cref="ProgressNotificationValue"/> values
///       are not included in the JSON schema and are bound to an <see cref="IProgress{ProgressNotificationValue}"/> instance manufactured
///       to forward progress notifications from the tool to the client. If the client included a <see cref="ProgressToken"/> in their request,
///       progress reports issued to this instance will propagate to the client as <see cref="NotificationMethods.ProgressNotification"/> notifications with
///       that token. If the client did not include a <see cref="ProgressToken"/>, the instance will ignore any progress reports issued to it.
///     </description>
///   </item>
///   <item>
///     <description>
///       When the <see cref="McpServerTool"/> is constructed, it may be passed an <see cref="IServiceProvider"/> via
///       <see cref="McpServerToolCreateOptions.Services"/>. Any parameter that can be satisfied by that <see cref="IServiceProvider"/>
///       according to <see cref="IServiceProviderIsService"/> will not be included in the generated JSON schema and will be resolved
///       from the <see cref="IServiceProvider"/> provided to <see cref="InvokeAsync"/> rather than from the argument collection.
///     </description>
///   </item>
///   <item>
///     <description>
///       Any parameter attributed with <see cref="FromKeyedServicesAttribute"/> will similarly be resolved from the
///       <see cref="IServiceProvider"/> provided to <see cref="InvokeAsync"/> rather than from the argument
///       collection, and will not be included in the generated JSON schema.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// All other parameters are deserialized from the <see cref="JsonElement"/>s in the <see cref="CallToolRequestParams.Arguments"/> dictionary,
/// using the <see cref="JsonSerializerOptions"/> supplied in <see cref="McpServerToolCreateOptions.SerializerOptions"/>, or if none was provided,
/// using <see cref="McpJsonUtilities.DefaultOptions"/>.
/// </para>
/// <para>
/// In general, the data supplied via the <see cref="CallToolRequestParams.Arguments"/>'s dictionary is passed along from the caller and
/// should thus be considered unvalidated and untrusted. To provide validated and trusted data to the invocation of the tool, consider having
/// the tool be an instance method, referring to data stored in the instance, or using an instance or parameters resolved from the <see cref="IServiceProvider"/>
/// to provide data to the method.
/// </para>
/// <para>
/// The tool method is responsible for validating its own input arguments (e.g., checking required fields, value ranges, string lengths, or
/// any other business rules). Data annotations such as <c>RequiredAttribute</c> and
/// <c>MaxLengthAttribute</c> on parameter types influence the generated JSON schema exposed
/// to clients, but they are not enforced at runtime by the SDK. Validation should be performed explicitly within the tool method.
/// </para>
/// <para>
/// To signal an error (including validation failures) back to the client, either throw an <see cref="McpException"/>
/// or return a <see cref="CallToolResult"/> with <see cref="CallToolResult.IsError"/> set to <see langword="true"/>.
/// When a tool throws an <see cref="McpException"/>, its <see cref="Exception.Message"/> is included in the error result
/// sent to the client. Throwing any other exception type also results in an error <see cref="CallToolResult"/>, but with
/// a generic error message (to avoid leaking sensitive information). Alternatively, a tool can declare a return type of
/// <see cref="CallToolResult"/> to have full control over both success and error responses.
/// </para>
/// <para>
/// It is important to provide clear <see cref="System.ComponentModel.DescriptionAttribute"/> values on tool methods and their parameters.
/// These descriptions are surfaced to AI models and help them determine when and how to use the tool, what values to pass for each parameter,
/// and what constraints the parameters have. Well-written descriptions reduce incorrect tool invocations and improve the quality of
/// model interactions.
/// </para>
/// <para>
/// Return values from a method are used to create the <see cref="CallToolResult"/> that is sent back to the client:
/// </para>
/// <list type="table">
///   <item>
///     <term><see langword="null"/></term>
///     <description>Returns an empty <see cref="CallToolResult.Content"/> list.</description>
///   </item>
///   <item>
///     <term><see cref="AIContent"/></term>
///     <description>Converted to a single <see cref="ContentBlock"/> object using <see cref="AIContentExtensions.ToContentBlock(AIContent, JsonSerializerOptions)"/>.</description>
///   </item>
///   <item>
///     <term><see cref="string"/></term>
///     <description>Converted to a single <see cref="TextContentBlock"/> object with its text set to the string value.</description>
///   </item>
///   <item>
///     <term><see cref="ContentBlock"/></term>
///     <description>Returned as a single-item <see cref="ContentBlock"/> list.</description>
///   </item>
///   <item>
///     <term><see cref="IEnumerable{AIContent}"/> of <see cref="AIContent"/></term>
///     <description>Each <see cref="AIContent"/> is converted to a <see cref="ContentBlock"/> object using <see cref="AIContentExtensions.ToContentBlock(AIContent, JsonSerializerOptions)"/>.</description>
///   </item>
///   <item>
///     <term><see cref="IEnumerable{ContentBlock}"/> of <see cref="ContentBlock"/></term>
///     <description>Returned as the <see cref="ContentBlock"/> list.</description>
///   </item>
///   <item>
///     <term><see cref="CallToolResult"/></term>
///     <description>Returned directly without modification.</description>
///   </item>
///   <item>
///     <term>Other types</term>
///     <description>Serialized to JSON and returned as a single <see cref="ContentBlock"/> object with <see cref="ContentBlock.Type"/> set to "text".</description>
///   </item>
/// </list>
/// </remarks>
public abstract class McpServerTool : IMcpServerPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="McpServerTool"/> class.</summary>
    protected McpServerTool()
    {
    }

    /// <summary>Gets the protocol <see cref="Tool"/> type for this instance.</summary>
    public abstract Tool ProtocolTool { get; }

    /// <summary>
    /// Gets the metadata for this tool instance.
    /// </summary>
    /// <remarks>
    /// Contains attributes from the associated MethodInfo and declaring class (if any),
    /// with class-level attributes appearing before method-level attributes.
    /// </remarks>
    public abstract IReadOnlyList<object> Metadata { get; }

    /// <summary>Invokes the <see cref="McpServerTool"/>.</summary>
    /// <param name="request">The request information resulting in the invocation of this tool.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The call response from invoking the tool.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public abstract ValueTask<CallToolResult> InvokeAsync(
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
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
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
    /// Creates an <see cref="McpServerTool"/> instance for a method, specified via a <see cref="MethodInfo"/> for
    /// an instance method, along with a <see cref="Type"/> representing the type of the target object to
    /// instantiate each time the method is invoked.
    /// </summary>
    /// <param name="method">The instance method to be represented via the created <see cref="McpServerTool"/>.</param>
    /// <param name="createTargetFunc">
    /// Callback used on each function invocation to create an instance of the type on which the instance method <paramref name="method"/>
    /// will be invoked. If the returned instance is <see cref="IAsyncDisposable"/> or <see cref="IDisposable"/>, it will
    /// be disposed of after method completes its invocation.
    /// </param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerTool"/> to control its behavior.</param>
    /// <returns>The created <see cref="McpServerTool"/> for invoking <paramref name="method"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> or <paramref name="createTargetFunc"/> is <see langword="null"/>.</exception>
    public static McpServerTool Create(
        MethodInfo method,
        Func<RequestContext<CallToolRequestParams>, object> createTargetFunc,
        McpServerToolCreateOptions? options = null) =>
        AIFunctionMcpServerTool.Create(method, createTargetFunc, options);

    /// <summary>Creates an <see cref="McpServerTool"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    /// <param name="function">The function to wrap.</param>
    /// <param name="options">Optional options used in the creation of the <see cref="McpServerTool"/> to control its behavior.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Unlike the other overloads of Create, the <see cref="McpServerTool"/> created by <see cref="Create(AIFunction, McpServerToolCreateOptions)"/>
    /// does not provide all of the special parameter handling for MCP-specific concepts, like <see cref="McpServer"/>.
    /// </remarks>
    public static McpServerTool Create(
        AIFunction function,
        McpServerToolCreateOptions? options = null) =>
        AIFunctionMcpServerTool.Create(function, options);

    /// <inheritdoc />
    public override string ToString() => ProtocolTool.Name;

    /// <inheritdoc />
    string IMcpServerPrimitive.Id => ProtocolTool.Name;
}
