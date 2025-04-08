using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils.Json;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Collections.ObjectModel;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides an AI function that calls a tool through the Model Context Protocol via <see cref="IMcpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="McpClientTool"/> class encapsulates a tool available through the Model Context Protocol (MCP) server,
/// allowing it to be invoked as an <see cref="AIFunction"/>. This enables integration with AI models
/// that support function calling capabilities.
/// </para>
/// <para>
/// Tools retrieved from an MCP server can be customized for model presentation using methods like
/// <see cref="WithName"/> and <see cref="WithDescription"/> without changing the underlying tool functionality.
/// </para>
/// <para>
/// Typically, you would get instances of this class by calling <c>ListToolsAsync</c> or <c>EnumerateToolsAsync</c>
/// extension methods on an <see cref="IMcpClient"/> instance.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Get available tools from an MCP client
/// var tools = await mcpClient.ListToolsAsync();
/// 
/// // Find a specific tool
/// var calculatorTool = tools.FirstOrDefault(t => t.Name == "Calculator");
/// if (calculatorTool != null)
/// {
///     // Optionally customize the tool presentation to the model
///     var customTool = calculatorTool.WithName("Math").WithDescription("Performs basic math operations");
///     
///     // Invoke the tool
///     var result = await customTool.InvokeAsync(
///         new Dictionary&lt;string, object?&gt; { ["x"] = 10, ["y"] = 5 }, 
///         CancellationToken.None);
/// }
/// </code>
/// </example>
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
    /// Creates a new instance of the tool with the specified name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is useful for optimizing the tool name for specific models or for prefixing the tool name 
    /// with a (usually server-derived) namespace to avoid conflicts.
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
    /// The server will still be called with the original tool name, so no mapping is required on the server side.
    /// </para>
    /// </remarks>
    /// <param name="name">The model-facing name to give the tool.</param>
    /// <returns>A new instance of <see cref="McpClientTool"/> with the provided name.</returns>
    /// <example>
    /// <code>
    /// // Get a calculator tool from an MCP client
    /// var tools = await mcpClient.ListToolsAsync();
    /// var calculatorTool = tools.FirstOrDefault(t => t.Name == "Calculator");
    /// 
    /// // Create a new instance with a more specific name
    /// var mathTool = calculatorTool.WithName("Math");
    /// 
    /// // You can chain with other customization methods
    /// var customTool = calculatorTool.WithName("Math").WithDescription("Performs basic math operations");
    /// </code>
    /// </example>
    public McpClientTool WithName(string name)
    {
        return new McpClientTool(_client, ProtocolTool, JsonSerializerOptions, name, _description);
    }

    /// <summary>
    /// Creates a new instance of the tool with the specified description.
    /// This can be used to provide modified or additional (e.g. examples) context to the model about the tool.
    /// </summary>
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
    /// This will in general require a hard-coded mapping in the client.
    /// It is not recommended to use this without running evaluations to ensure the model actually benefits from the custom description.
    /// </para>
    /// </remarks>
    /// <param name="description">The description to give the tool.</param>
    /// <returns>A new instance of <see cref="McpClientTool"/> with the provided description.</returns>
    /// <example>
    /// <code>
    /// // Get a calculator tool from an MCP client
    /// var tools = await mcpClient.ListToolsAsync();
    /// var calculatorTool = tools.FirstOrDefault(t => t.Name == "Calculator");
    /// 
    /// // Create a new instance with a more descriptive explanation
    /// var enhancedTool = calculatorTool.WithDescription("Performs arithmetic operations like addition, subtraction, multiplication and division between two numbers.");
    /// 
    /// // You can chain with other customization methods
    /// var customTool = calculatorTool.WithName("Math").WithDescription("Performs basic math operations");
    /// </code>
    /// </example>
    public McpClientTool WithDescription(string description)
    {
        return new McpClientTool(_client, ProtocolTool, JsonSerializerOptions, _name, description);
    }

    /// <summary>
    /// Gets the protocol <see cref="Tool"/> type for this instance.
    /// </summary>
    /// <remarks>
    /// This property provides access to the underlying Tool as defined in the Model Context Protocol.
    /// It contains the original metadata about the tool as provided by the server, including its
    /// name, description, and schema information before any customizations applied through methods
    /// like <see cref="WithName"/> or <see cref="WithDescription"/>.
    /// </remarks>
    public Tool ProtocolTool { get; }

    /// <inheritdoc/>
    /// <summary>
    /// Gets the name of this tool as it should be presented to the AI model.
    /// </summary>
    /// <remarks>
    /// This is either the original name from the protocol tool or a custom name if
    /// <see cref="WithName"/> was used to create this instance.
    /// </remarks>
    public override string Name => _name;

    /// <inheritdoc/>
    /// <summary>
    /// Gets the description of this tool as it should be presented to the AI model.
    /// </summary>
    /// <remarks>
    /// This is either the original description from the protocol tool or a custom description if
    /// <see cref="WithDescription"/> was used to create this instance.
    /// </remarks>
    public override string Description => _description;

    /// <inheritdoc/>
    /// <summary>
    /// Gets the JSON schema for this tool, represented as a <see cref="JsonElement"/>.
    /// </summary>
    /// <remarks>
    /// This property returns the input schema of the underlying protocol tool. The schema follows JSON Schema standards
    /// and describes the parameters that this tool accepts, including their types, descriptions, and validation requirements.
    /// AI models use this schema to understand how to properly format arguments when invoking the tool.
    /// </remarks>
    public override JsonElement JsonSchema => ProtocolTool.InputSchema;

    /// <inheritdoc/>
    /// <summary>
    /// Gets the JSON serialization options used for serializing and deserializing data when invoking this tool.
    /// </summary>
    /// <remarks>
    /// These serialization options are used when converting between .NET objects and JSON when communicating 
    /// with the Model Context Protocol server. This includes serializing arguments when making tool calls and 
    /// deserializing responses when processing results.
    /// </remarks>
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
}