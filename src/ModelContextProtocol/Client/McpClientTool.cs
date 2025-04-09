using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils.Json;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Collections.ObjectModel;

namespace ModelContextProtocol.Client;

/// <summary>Provides an AI function that calls a tool through <see cref="IMcpClient"/>.</summary>
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
    /// This is useful for optimizing the tool name for specific models or for prefixing the tool name with a (usually server-derived) namespace to avoid conflicts.
    /// The server will still be called with the original tool name, so no mapping is required.
    /// </summary>
    /// <param name="name">The model-facing name to give the tool.</param>
    /// <returns>Copy of this McpClientTool with the provided name</returns>
    public McpClientTool WithName(string name)
    {
        return new McpClientTool(_client, ProtocolTool, JsonSerializerOptions, name, _description);
    }

    /// <summary>
    /// Creates a new instance of the tool with the specified description.
    /// This can be used to provide modified or additional (e.g. examples) context to the model about the tool.
    /// This will in general require a hard-coded mapping in the client. 
    /// It is not recommended to use this without running evaluations to ensure the model actually benefits from the custom description.
    /// </summary>
    /// <param name="description">The description to give the tool.</param>
    /// <returns>Copy of this McpClientTool with the provided description</returns>
    public McpClientTool WithDescription(string description)
    {
        return new McpClientTool(_client, ProtocolTool, JsonSerializerOptions, _name, description);
    }

    /// <summary>Gets the protocol <see cref="Tool"/> type for this instance.</summary>
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
}