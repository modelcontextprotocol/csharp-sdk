using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Used to indicate that a method should be considered an <see cref="McpServerPrompt"/>.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is applied to methods that should be exposed as prompts in the Model Context Protocol. When a class
/// containing methods marked with this attribute is registered with McpServerBuilderExtensions,
/// these methods become available as prompts that can be called by MCP clients.
/// </para>
/// <para>
/// When methods are provided directly to <see cref="M:McpServerPrompt.Create"/>, the attribute is not required.
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
///       according to <see cref="IServiceProviderIsService"/> will be resolved from the <see cref="IServiceProvider"/> provided to the
///       prompt invocation rather than from the argument collection.
///     </description>
///   </item>
///   <item>
///     <description>
///       Any parameter attributed with <see cref="FromKeyedServicesAttribute"/> will similarly be resolved from the
///       <see cref="IServiceProvider"/> provided to the prompt invocation rather than from the argument collection.
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
/// </list>
/// <para>
/// Other returned types will result in an <see cref="InvalidOperationException"/> being thrown.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpServerPromptAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerPromptAttribute"/> class.
    /// </summary>
    public McpServerPromptAttribute()
    {
    }

    /// <summary>Gets or sets the name of the prompt.</summary>
    /// <remarks>If <see langword="null"/>, the method name is used.</remarks>
    public string? Name { get; set; }

    /// <summary>Gets or sets the title of the prompt.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the source URI for the prompt's icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value can be an HTTP/HTTPS URL pointing to an image file or a data URI with base64-encoded image data.
    /// When specified, a single icon will be added to the prompt.
    /// </para>
    /// <para>
    /// For more advanced icon configuration (multiple icons, MIME type specification, size characteristics),
    /// use <see cref="McpServerPromptCreateOptions.Icons"/> when creating the prompt programmatically.
    /// </para>
    /// </remarks>
    public string? IconSource { get; set; }
}
