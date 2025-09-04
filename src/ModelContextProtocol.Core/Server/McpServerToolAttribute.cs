using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Used to indicate that a method should be considered an <see cref="McpServerTool"/>.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is applied to methods that should be exposed as tools in the Model Context Protocol. When a class 
/// containing methods marked with this attribute is registered with McpServerBuilderExtensions,
/// these methods become available as tools that can be called by MCP clients.
/// </para>
/// <para>
/// When methods are provided directly to <see cref="M:McpServerTool.Create"/>, the attribute is not required.
/// </para>
/// <para>
/// By default, parameters are sourced from the <see cref="CallToolRequestParams.Arguments"/> dictionary, which is a collection
/// of key/value pairs, and are represented in the JSON schema for the function, as exposed in the returned <see cref="McpServerTool"/>'s
/// <see cref="McpServerTool.ProtocolTool"/>'s <see cref="Tool.InputSchema"/>. Those parameters are deserialized from the
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
///       from the <see cref="IServiceProvider"/> provided to when the tool is invoked rather than from the argument collection.
///     </description>
///   </item>
///   <item>
///     <description>
///       Any parameter attributed with <see cref="FromKeyedServicesAttribute"/> will similarly be resolved from the 
///       <see cref="IServiceProvider"/> provided when the tool is invoked rather than from the argument
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
/// Return values from a method are used to create the <see cref="CallToolResult"/> that is sent back to the client:
/// </para>
/// <list type="table">
///   <item>
///     <term><see langword="null"/></term>
///     <description>Returns an empty <see cref="CallToolResult.Content"/> list.</description>
///   </item>
///   <item>
///     <term><see cref="AIContent"/></term>
///     <description>Converted to a single <see cref="ContentBlock"/> object using <see cref="AIContentExtensions.ToContent(AIContent)"/>.</description>
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
///     <term><see cref="IEnumerable{String}"/> of <see cref="string"/></term>
///     <description>Each <see cref="string"/> is converted to a <see cref="ContentBlock"/> object with its text set to the string value.</description>
///   </item>
///   <item>
///     <term><see cref="IEnumerable{AIContent}"/> of <see cref="AIContent"/></term>
///     <description>Each <see cref="AIContent"/> is converted to a <see cref="ContentBlock"/> object using <see cref="AIContentExtensions.ToContent(AIContent)"/>.</description>
///   </item>
///   <item>
///     <term><see cref="IEnumerable{Content}"/> of <see cref="ContentBlock"/></term>
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
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpServerToolAttribute : Attribute
{
    // Defaults based on the spec
    private const bool DestructiveDefault = true;
    private const bool IdempotentDefault = false;
    private const bool OpenWorldDefault = true;
    private const bool ReadOnlyDefault = false;

    // Nullable backing fields so we can distinguish
    internal bool? _destructive;
    internal bool? _idempotent;
    internal bool? _openWorld;
    internal bool? _readOnly;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerToolAttribute"/> class.
    /// </summary>
    public McpServerToolAttribute()
    {
    }

    /// <summary>Gets the name of the tool.</summary>
    /// <remarks>If <see langword="null"/>, the method name will be used.</remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a human-readable title for the tool that can be displayed to users.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The title provides a more descriptive, user-friendly name for the tool than the tool's
    /// programmatic name. It is intended for display purposes and to help users understand
    /// the tool's purpose at a glance.
    /// </para>
    /// <para>
    /// Unlike the tool name (which follows programmatic naming conventions), the title can
    /// include spaces, special characters, and be phrased in a more natural language style.
    /// </para>
    /// </remarks>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets whether the tool may perform destructive updates to its environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <see langword="true"/>, the tool may perform destructive updates to its environment.
    /// If <see langword="false"/>, the tool performs only additive updates.
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// </para>
    /// <para>
    /// The default is <see langword="true"/>.
    /// </para>
    /// </remarks>
    public bool Destructive 
    {
        get => _destructive ?? DestructiveDefault; 
        set => _destructive = value; 
    }

    /// <summary>
    /// Gets or sets whether calling the tool repeatedly with the same arguments 
    /// will have no additional effect on its environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// </para>
    /// <para>
    /// The default is <see langword="false"/>.
    /// </para>
    /// </remarks>
    public bool Idempotent  
    {
        get => _idempotent ?? IdempotentDefault;
        set => _idempotent = value; 
    }

    /// <summary>
    /// Gets or sets whether this tool may interact with an "open world" of external entities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <see langword="true"/>, the tool may interact with an unpredictable or dynamic set of entities (like web search).
    /// If <see langword="false"/>, the tool's domain of interaction is closed and well-defined (like memory access).
    /// </para>
    /// <para>
    /// The default is <see langword="true"/>.
    /// </para>
    /// </remarks>
    public bool OpenWorld
    {
        get => _openWorld ?? OpenWorldDefault; 
        set => _openWorld = value; 
    }

    /// <summary>
    /// Gets or sets whether this tool does not modify its environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <see langword="true"/>, the tool only performs read operations without changing state.
    /// If <see langword="false"/>, the tool may make modifications to its environment.
    /// </para>
    /// <para>
    /// Read-only tools do not have side effects beyond computational resource usage.
    /// They don't create, update, or delete data in any system.
    /// </para>
    /// <para>
    /// The default is <see langword="false"/>.
    /// </para>
    /// </remarks>
    public bool ReadOnly  
    {
        get => _readOnly ?? ReadOnlyDefault; 
        set => _readOnly = value; 
    }

    /// <summary>
    /// Gets or sets whether the tool should report an output schema for structured content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When enabled, the tool will attempt to populate the <see cref="Tool.OutputSchema"/>
    /// and provide structured content in the <see cref="CallToolResult.StructuredContent"/> property.
    /// </para>
    /// <para>
    /// The default is <see langword="false"/>.
    /// </para>
    /// </remarks>
    public bool UseStructuredContent { get; set; }
}
