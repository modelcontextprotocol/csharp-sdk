using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils.Json;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Collections.ObjectModel;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides an <see cref="AIFunction"/> that calls a tool via an <see cref="IMcpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="McpClientTool"/> class encapsulates an <see cref="IMcpClient"/> along with a description of 
/// a tool available via that client, allowing it to be invoked as an <see cref="AIFunction"/>. This enables integration
/// with AI models that support function calling capabilities.
/// </para>
/// <para>
/// Tools retrieved from an MCP server can be customized for model presentation using methods like
/// <see cref="WithName"/> and <see cref="WithDescription"/> without changing the underlying tool functionality.
/// </para>
/// <para>
/// Typically, you would get instances of this class by calling the <see cref="McpClientExtensions.ListToolsAsync"/>
/// or <see cref="McpClientExtensions.EnumerateToolsAsync"/> extension methods on an <see cref="IMcpClient"/> instance.
/// </para>
/// </remarks>
public sealed class McpClientTool : AIFunction
{
    /// <summary>Additional properties exposed from tools.</summary>
    private static readonly ReadOnlyDictionary<string, object?> s_additionalProperties =
        new(new Dictionary<string, object?>()
        {
            ["Strict"] = false, // some MCP schemas may not meet "strict" requirements
        });

    private readonly IMcpClient _client;
    private readonly string _name;
    private readonly string _description;

    internal McpClientTool(IMcpClient client, Tool tool, JsonSerializerOptions serializerOptions, string? name = null, string? description = null)
    {
        _client = client;
        ProtocolTool = tool;
        JsonSerializerOptions = serializerOptions;
        _name = name ?? tool.Name;
        _description = description ?? tool.Description ?? string.Empty;
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

    /// <inheritdoc/>
    public override string Description => _description;

    /// <inheritdoc/>
    public override JsonElement JsonSchema => ProtocolTool.InputSchema;

    /// <inheritdoc/>
    public override JsonSerializerOptions JsonSerializerOptions { get; }

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => s_additionalProperties;

    /// <inheritdoc/>
    protected async override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        CallToolResponse result = await _client.CallToolAsync(ProtocolTool.Name, arguments, JsonSerializerOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.CallToolResponse);
    }

    /// <summary>
    /// Creates a new instance of the tool but modified to return the specified name from its <see cref="Name"/> property.
    /// </summary>
    /// <param name="name">The model-facing name to give the tool.</param>
    /// <returns>A new instance of <see cref="McpClientTool"/> with the provided name.</returns>
    /// <remarks>
    /// <para>
    /// This is useful for optimizing the tool name for specific models or for prefixing the tool name 
    /// with a namespace to avoid conflicts.
    /// </para>
    /// <para>
    /// Changing the name can help with:
    /// </para>
    /// <list type="bullet">
    ///   <item>Making the tool name more intuitive for the model</item>
    ///   <item>Preventing name collisions when using tools from multiple sources</item>
    ///   <item>Creating specialized versions of a general tool for specific contexts</item>
    /// </list>
    /// <para>
    /// When invoking <see cref="AIFunction.InvokeAsync"/>, the MCP server will still be called with 
    /// the original tool name, so no mapping is required on the server side. This new name only affects
    /// the value returned from this instance's <see cref="AITool.Name"/>.
    /// </para>
    /// </remarks>
    public McpClientTool WithName(string name)
    {
        return new McpClientTool(_client, ProtocolTool, JsonSerializerOptions, name, _description);
    }

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
    ///   <item>The original description is too technical or lacks clarity for the model</item>
    ///   <item>You want to add example usage scenarios to improve the model's understanding</item>
    ///   <item>You need to tailor the tool's description for specific model requirements</item>
    /// </list>
    /// <para>
    /// When invoking <see cref="AIFunction.InvokeAsync"/>, the MCP server will still be called with 
    /// the original tool description, so no mapping is required on the server side. This new description only affects
    /// the value returned from this instance's <see cref="AITool.Description"/>.
    /// </para>
    /// </remarks>
    /// <returns>A new instance of <see cref="McpClientTool"/> with the provided description.</returns>
    public McpClientTool WithDescription(string description)
    {
        return new McpClientTool(_client, ProtocolTool, JsonSerializerOptions, _name, description);
    }
}