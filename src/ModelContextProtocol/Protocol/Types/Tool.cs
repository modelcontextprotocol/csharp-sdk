using ModelContextProtocol.Utils.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a tool that the server is capable of calling. Part of the ListToolsResponse.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class Tool
{
    private JsonElement _inputSchema = McpJsonUtilities.DefaultMcpToolSchema;

    /// <summary>
    /// The name of the tool.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A human-readable description of the tool.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// A JSON Schema object defining the expected parameters for the tool.
    /// </summary>
    /// <remarks>
    /// Needs to a valid JSON schema object that additionally is of type object.
    /// </remarks>
    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema 
    { 
        get => _inputSchema; 
        set
        {
            if (!McpJsonUtilities.IsValidMcpToolSchema(value))
            {
                throw new ArgumentException("The specified document is not a valid MCP tool JSON schema.", nameof(InputSchema));
            }

            _inputSchema = value;
        }
    }

    /// <summary>
    /// Optional additional tool information.
    /// </summary>
    [JsonPropertyName("annotations")]
    public ToolAnnotations? Annotations { get; set; }
}
