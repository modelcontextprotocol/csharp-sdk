using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils.Json;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>Provides an AI function that calls a tool through <see cref="IMcpClient"/>.</summary>
public sealed class McpClientTool : AIFunction
{
    private readonly IMcpClient _client;
    private readonly Tool _tool;

    internal McpClientTool(IMcpClient client, Tool tool)
    {
        _client = client;
        _tool = tool;
    }

    /// <inheritdoc/>
    public override string Name => _tool.Name;

    /// <inheritdoc/>
    public override string Description => _tool.Description ?? string.Empty;

    /// <inheritdoc/>
    public override JsonElement JsonSchema => _tool.InputSchema;

    /// <inheritdoc/>
    public override JsonSerializerOptions JsonSerializerOptions => McpJsonUtilities.DefaultOptions;

    /// <inheritdoc/>
    protected async override Task<object?> InvokeCoreAsync(
        IEnumerable<KeyValuePair<string, object?>> arguments, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, object?> argDict =
            arguments as IReadOnlyDictionary<string, object?> ??
            arguments.ToDictionary();

        CallToolResponse result = await _client.CallToolAsync(_tool.Name, argDict, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.CallToolResponse);
    }
}