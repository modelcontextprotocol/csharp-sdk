using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils.Json;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>Provides an AI function that calls a tool through <see cref="IMcpClient"/>.</summary>
public sealed class McpClientTool : AIFunction
{
    private readonly IMcpClient _client;
    private readonly string? _nameOverride;
    private readonly string? _descriptionOverride;

    internal McpClientTool(IMcpClient client, Tool tool, string? nameOverride = null, string? descriptionOverride = null)
    {
        _client = client;
        ProtocolTool = tool;
        _nameOverride = nameOverride;
        _descriptionOverride = descriptionOverride;
    }

    /// <summary>
    /// Creates a new instance of the tool with the specified name.
    /// This is useful for optimizing the tool name for specific models or for prefixing the tool name with a (usually server-derived) namespace to avoid conflicts.
    /// The server will still be called with the original tool name, so no mapping is required.
    /// </summary>
    /// <param name="name">The model-facing name to give the tool</param>
    /// <returns>Equivalent McpClientTool, but with the provided name</returns>
    public McpClientTool WithName(string name)
    {
        return new McpClientTool(_client, ProtocolTool, name, _descriptionOverride);
    }

    /// <summary>
    /// Creates a new instance of the tool with the specified description.
    /// This is can be used to provide modified or additional (e.g. examples) context to the model about the tool.
    /// This will in general require a hard-coded mapping in the client. 
    /// It is not recommended to use this without running evaluations to ensure the model actually benefits from the custom description.
    /// </summary>
    /// <param name="description"></param>
    /// <returns></returns>
    public McpClientTool WithDescription(string description)
    {
        return new McpClientTool(_client, ProtocolTool, _nameOverride, description);
    }

    /// <summary>Gets the protocol <see cref="Tool"/> type for this instance.</summary>
    public Tool ProtocolTool { get; }

    /// <inheritdoc/>
    public override string Name => _nameOverride ?? ProtocolTool.Name;

    /// <inheritdoc/>
    public override string Description => _descriptionOverride ?? ProtocolTool.Description ?? string.Empty;

    /// <inheritdoc/>
    public override JsonElement JsonSchema => ProtocolTool.InputSchema;

    /// <inheritdoc/>
    public override JsonSerializerOptions JsonSerializerOptions => McpJsonUtilities.DefaultOptions;

    /// <inheritdoc/>
    protected async override Task<object?> InvokeCoreAsync(
        IEnumerable<KeyValuePair<string, object?>> arguments, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, object?> argDict =
            arguments as IReadOnlyDictionary<string, object?> ??
            arguments.ToDictionary();

        CallToolResponse result = await _client.CallToolAsync(ProtocolTool.Name, argDict, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.CallToolResponse);
    }    
}