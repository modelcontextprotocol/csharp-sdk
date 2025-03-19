using Anthropic.SDK.Common;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol;

public static class McpToolExtensions
{
    public static IList<Anthropic.SDK.Common.Tool> ToAnthropicTools(this IEnumerable<ModelContextProtocol.Protocol.Types.Tool> tools)
    {
        if (tools is null)
        {
            throw new ArgumentNullException(nameof(tools));
        }

        List<Anthropic.SDK.Common.Tool> result = [];
        foreach (var tool in tools)
        {
            var function = new Function(tool.Name, tool.Description, JsonSerializer.SerializeToNode(tool.InputSchema));
            result.Add(function);
        }
        return result;
    }

    public static Dictionary<string, object>? ToMCPArguments(this JsonNode jsonNode)
    {
        if (jsonNode == null)
            return null;

        // Convert JsonNode to Dictionary<string, object>
        return jsonNode.AsObject()
            .ToDictionary(
                prop => prop.Key,
                prop => JsonSerializer.Deserialize<object>(prop.Value)!
            );
    }
}