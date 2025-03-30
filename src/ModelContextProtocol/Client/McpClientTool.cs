using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils.Json;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>Provides an AI function that calls a tool through <see cref="IMcpClient"/>.</summary>
public sealed class McpClientTool : AIFunction
{
    private readonly IMcpClient _client;

    internal McpClientTool(IMcpClient client, Tool tool)
    {
        _client = client;
        ProtocolTool = tool;
    }

    /// <summary>Gets the protocol <see cref="Tool"/> type for this instance.</summary>
    public Tool ProtocolTool { get; }

    /// <inheritdoc/>
    public override string Name => ProtocolTool.Name;

    /// <inheritdoc/>
    public override string Description => ProtocolTool.Description ?? string.Empty;

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