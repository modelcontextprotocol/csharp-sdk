using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides an <see cref="AIFunction"/> that calls a tool via an <see cref="McpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="McpClientTool"/> class encapsulates an <see cref="McpClient"/> along with a description of
/// a tool available via that client, allowing it to be invoked as an <see cref="AIFunction"/>. This enables integration
/// with AI models that support function calling capabilities.
/// </para>
/// <para>
/// Tools retrieved from an MCP server can be customized for model presentation using methods like
/// <see cref="WithName"/> and <see cref="WithDescription"/> without changing the underlying tool functionality.
/// </para>
/// <para>
/// Typically, you would get instances of this class by calling the <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/>
/// method on an <see cref="McpClient"/> instance.
/// </para>
/// </remarks>
public sealed class McpClientTool : AIFunction
{
    private readonly McpClient _client;
    private readonly string _name;
    private readonly string _description;
    private readonly IProgress<ProgressNotificationValue>? _progress;
    private readonly JsonObject? _meta;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClientTool"/> class.
    /// </summary>
    /// <param name="client">The <see cref="McpClient"/> instance to use for invoking the tool.</param>
    /// <param name="tool">The protocol <see cref="Tool"/> definition describing the tool's metadata and schema.</param>
    /// <param name="serializerOptions">
    /// The JSON serialization options governing argument serialization. If <see langword="null"/>,
    /// <see cref="McpJsonUtilities.DefaultOptions"/> is used.
    /// </param>
    /// <remarks>
    /// <para>
    /// This constructor enables reusing cached tool definitions across different <see cref="McpClient"/> instances
    /// without needing to call <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/> on every reconnect. 
    /// This is particularly useful in scenarios where tool definitions are stable and network round-trips should be minimized.
    /// </para>
    /// <para>
    /// The provided <paramref name="tool"/> must represent a tool that is actually available on the server
    /// associated with the <paramref name="client"/>. Attempting to invoke a tool that doesn't exist on the
    /// server will result in an <see cref="McpException"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="tool"/> is <see langword="null"/>.</exception>
    public McpClientTool(
        McpClient client,
        Tool tool,
        JsonSerializerOptions? serializerOptions = null)
    {
        Throw.IfNull(client);
        Throw.IfNull(tool);

        _client = client;
        ProtocolTool = tool;
        JsonSerializerOptions = serializerOptions ?? McpJsonUtilities.DefaultOptions;
        _name = tool.Name;
        _description = tool.Description ?? string.Empty;
        _progress = null;
        _meta = null;
    }

    internal McpClientTool(
        McpClient client,
        Tool tool,
        JsonSerializerOptions serializerOptions,
        string? name = null,
        string? description = null,
        IProgress<ProgressNotificationValue>? progress = null,
        JsonObject? meta = null)
    {
        _client = client;
        ProtocolTool = tool;
        JsonSerializerOptions = serializerOptions;
        _name = name ?? tool.Name;
        _description = description ?? tool.Description ?? string.Empty;
        _progress = progress;
        _meta = meta;
    }

    /// <summary>
    /// Gets the protocol <see cref="Tool"/> type for this instance.
    /// </summary>
    /// <remarks>
    /// This property provides direct access to the underlying protocol representation of the tool,
    /// which can be useful for advanced scenarios or when implementing custom MCP client extensions.
    /// It contains the original metadata about the tool as provided by the server, including its
    /// name, description, and schema information before any customizations applied through methods
    /// like <see cref="WithName"/> or <see cref="WithDescription"/>.
    /// </remarks>
    public Tool ProtocolTool { get; }

    /// <inheritdoc/>
    public override string Name => _name;

    /// <summary>Gets the tool's title.</summary>
    public string? Title => ProtocolTool.Title ?? ProtocolTool.Annotations?.Title;

    /// <inheritdoc/>
    public override string Description => _description;

    /// <inheritdoc/>
    public override JsonElement JsonSchema => ProtocolTool.InputSchema;

    /// <inheritdoc/>
    public override JsonElement? ReturnJsonSchema => ProtocolTool.OutputSchema;

    /// <inheritdoc/>
    public override JsonSerializerOptions JsonSerializerOptions { get; }

    /// <inheritdoc/>
    protected async override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var options = JsonSerializerOptions is null ? null : new RequestOptions()
        {
            JsonSerializerOptions = JsonSerializerOptions,
        };
        CallToolResult result = await CallAsync(arguments, _progress, options, cancellationToken).ConfigureAwait(false);

        // We want to translate the result content into AIContent, using AIContent as the exchange types, so
        // that downstream IChatClients can specialize handling based on the content (e.g. sending image content
        // back to the AI service as a multi-modal tool response). However, when there is additional information
        // carried by the CallToolResult outside of its ContentBlocks, just returning AIContent from those ContentBlocks
        // would lose that information. So, we only do the translation if there is no additional information to preserve.
        if (result.IsError is not true &&
            result.StructuredContent is null &&
            result.Meta is not { Count: > 0 })
        {
            switch (result.Content.Count)
            {
                case 1 when result.Content[0].ToAIContent(JsonSerializerOptions) is { } aiContent:
                    return aiContent;

                case > 1 when result.Content.Select(c => c.ToAIContent(JsonSerializerOptions)).ToArray() is { } aiContents && aiContents.All(static c => c is not null):
                    return aiContents;
            }
        }

        return JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.CallToolResult);
    }

    /// <summary>
    /// Invokes the tool on the server.
    /// </summary>
    /// <param name="arguments">An optional dictionary of arguments to pass to the tool. Each key represents a parameter name,
    /// and its associated value represents the argument value.
    /// </param>
    /// <param name="progress">
    /// An optional <see cref="IProgress{T}"/> to have progress notifications reported to it. Setting this to a non-<see langword="null"/>
    /// value will result in a progress token being included in the call, and any resulting progress notifications during the operation
    /// routed to this instance.
    /// </param>
    /// <param name="options">
    /// Optional request options including metadata, serialization settings, and progress tracking.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>
    /// A task containing the <see cref="CallToolResult"/> from the tool execution. The response includes
    /// the tool's output content, which can be structured data, text, or an error message.
    /// </returns>
    /// <remarks>
    /// The base <see cref="AIFunction.InvokeAsync"/> method is overridden to invoke this <see cref="CallAsync"/> method.
    /// The only difference in behavior is that <see cref="AIFunction.InvokeAsync"/> serializes the resulting <see cref="CallToolResult"/>
    /// such that the <see cref="object"/> returned is a <see cref="JsonElement"/> containing the serialized <see cref="CallToolResult"/>.
    /// This <see cref="CallAsync"/> method is intended to be called directly by user code, whereas the base <see cref="AIFunction.InvokeAsync"/>
    /// is intended to be used polymorphically via the base class, typically as part of an <see cref="IChatClient"/> operation.
    /// </remarks>
    /// <exception cref="McpException">The server could not find the requested tool, or the server encountered an error while processing the request.</exception>
    /// <example>
    /// <code language="csharp">
    /// var result = await tool.CallAsync(
    ///     new Dictionary&lt;string, object?&gt;
    ///     {
    ///         ["message"] = "Hello MCP!"
    ///     });
    /// </code>
    /// </example>
    public ValueTask<CallToolResult> CallAsync(
        IReadOnlyDictionary<string, object?>? arguments = null,
        IProgress<ProgressNotificationValue>? progress = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // If there's any metadata provided with WithMeta, we can't just pass along the options as-is,
        // and instead need to create new options that merges in _meta.
        if (_meta is { } meta)
        {
            // Create a new RequestOptions, as we're going to need to store a new JsonObject for Meta (either
            // _meta or _meta+options.Meta), and we don't want to mutate the user's options object.
            RequestOptions newOptions = options?.Clone() ?? new();

            // If we also have newOptions.Meta, merge that with _meta into a new JsonObject, preferring
            // the objects from newOptions.Meta in case of conflicts.
            if (newOptions.Meta is { } newOptionsMeta)
            {
                meta = (JsonObject)meta.DeepClone();
                foreach (var p in newOptionsMeta)
                {
                    meta[p.Key] = p.Value?.DeepClone();
                }
            }
            
            newOptions.Meta = meta;
            options = newOptions;
        }

        return _client.CallToolAsync(
            ProtocolTool.Name,
            arguments,
            progress,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Creates a new instance of the tool but modified to return the specified name from its <see cref="Name"/> property.
    /// </summary>
    /// <param name="name">The model-facing name to give the tool.</param>
    /// <returns>A new instance of <see cref="McpClientTool"/> with the provided name.</returns>
    /// <remarks>
    /// <para>
    /// This method is useful for optimizing the tool name for specific models or for prefixing the tool name
    /// with a namespace to avoid conflicts.
    /// </para>
    /// <para>
    /// Changing the name can help with:
    /// </para>
    /// <list type="bullet">
    ///   <item>Making the tool name more intuitive for the model.</item>
    ///   <item>Preventing name collisions when using tools from multiple sources.</item>
    ///   <item>Creating specialized versions of a general tool for specific contexts.</item>
    /// </list>
    /// <para>
    /// When invoking <see cref="AIFunction.InvokeAsync"/>, the MCP server will still be called with
    /// the original tool name, so no mapping is required on the server side. This new name only affects
    /// the value returned from this instance's <see cref="AITool.Name"/>.
    /// </para>
    /// </remarks>
    public McpClientTool WithName(string name) =>
        new(_client, ProtocolTool, JsonSerializerOptions, name, _description, _progress, _meta);

    /// <summary>
    /// Creates a new instance of the tool but modified to return the specified description from its <see cref="Description"/> property.
    /// </summary>
    /// <param name="description">The description to give the tool.</param>
    /// <remarks>
    /// <para>
    /// Changing the description can help the model better understand the tool's purpose or provide more
    /// context about how the tool should be used. This is particularly useful when:
    /// </para>
    /// <list type="bullet">
    ///   <item>The original description is too technical or lacks clarity for the model.</item>
    ///   <item>You want to add example usage scenarios to improve the model's understanding.</item>
    ///   <item>You need to tailor the tool's description for specific model requirements.</item>
    /// </list>
    /// <para>
    /// When invoking <see cref="AIFunction.InvokeAsync"/>, the MCP server will still be called with
    /// the original tool description, so no mapping is required on the server side. This new description only affects
    /// the value returned from this instance's <see cref="AITool.Description"/>.
    /// </para>
    /// </remarks>
    /// <returns>A new instance of <see cref="McpClientTool"/> with the provided description.</returns>
    public McpClientTool WithDescription(string description) =>
        new(_client, ProtocolTool, JsonSerializerOptions, _name, description, _progress, _meta);

    /// <summary>
    /// Creates a new instance of the tool but modified to report progress via the specified <see cref="IProgress{T}"/>.
    /// </summary>
    /// <param name="progress">The <see cref="IProgress{T}"/> to which progress notifications should be reported.</param>
    /// <remarks>
    /// <para>
    /// Adding an <see cref="IProgress{T}"/> to the tool does not impact how it is reported to any AI model.
    /// Rather, when the tool is invoked, the request to the MCP server will include a unique progress token,
    /// and any progress notifications issued by the server with that progress token while the operation is in
    /// flight will be reported to the <paramref name="progress"/> instance.
    /// </para>
    /// <para>
    /// Only one <see cref="IProgress{T}"/> can be specified at a time. Calling <see cref="WithProgress"/> again
    /// overwrites any previously specified progress instance.
    /// </para>
    /// </remarks>
    /// <returns>A new instance of <see cref="McpClientTool"/>, configured with the provided progress instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
    public McpClientTool WithProgress(IProgress<ProgressNotificationValue> progress)
    {
        Throw.IfNull(progress);

        return new McpClientTool(_client, ProtocolTool, JsonSerializerOptions, _name, _description, progress, _meta);
    }

    /// <summary>
    /// Creates a new instance of the tool but modified to include the specified metadata in tool call requests.
    /// </summary>
    /// <param name="meta">
    /// The metadata to include in tool call requests. This will be serialized as the <c>_meta</c> field
    /// in the JSON-RPC request parameters.
    /// </param>
    /// <remarks>
    /// <para>
    /// Adding metadata to the tool allows you to pass additional protocol-level information with each tool call.
    /// This can be useful for tracing, logging, or passing context information to the server.
    /// </para>
    /// <para>
    /// Only one metadata object can be specified at a time. Calling <see cref="WithMeta"/> again
    /// will overwrite any previously specified metadata object. If passed <see langword="null"/>,
    /// any previously supplied metadata will be removed.
    /// </para>
    /// <para>
    /// The metadata is passed through to the server as-is, merged with any protocol-level metadata
    /// such as progress tokens when <see cref="WithProgress"/> is also used. If a <see cref="RequestOptions"/>
    /// is passed to <see cref="CallAsync"/>, the metadata from both <paramref name="meta"/> and its
    /// <see cref="RequestOptions"/> will be merged, preferring values from the <see cref="RequestOptions"/> in
    /// case of conflicts.
    /// </para>
    /// </remarks>
    /// <returns>A new instance of <see cref="McpClientTool"/>, configured with the provided metadata.</returns>
    public McpClientTool WithMeta(JsonObject? meta) =>
        new McpClientTool(_client, ProtocolTool, JsonSerializerOptions, _name, _description, _progress, meta);
}
