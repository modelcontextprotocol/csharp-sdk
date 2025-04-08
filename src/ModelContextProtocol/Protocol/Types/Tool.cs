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
    /// <remarks>
    /// <para>
    /// This description helps the AI model understand what the tool does and when to use it.
    /// It should be clear, concise, and accurately describe the tool's purpose and functionality.
    /// </para>
    /// <para>
    /// The description is typically presented to AI models to help them determine when
    /// and how to use the tool based on user requests.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var tool = new Tool
    /// {
    ///     Name = "GetWeather",
    ///     Description = "Gets the current weather for a location.",
    ///     InputSchema = // JSON schema defining latitude and longitude parameters
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// A JSON Schema object defining the expected parameters for the tool.
    /// Used by clients to validate inputs before sending them to the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The schema must be a valid JSON Schema object with the "type" property set to "object".
    /// This is enforced by validation in the setter which will throw an <see cref="ArgumentException"/>
    /// if an invalid schema is provided.
    /// </para>
    /// <para>
    /// The schema typically defines the properties (parameters) that the tool accepts, 
    /// their types, and which ones are required. This helps AI models understand
    /// how to structure their calls to the tool.
    /// </para>
    /// <para>
    /// If not explicitly set, a default minimal schema of <c>{"type":"object"}</c> is used.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var weatherTool = new Tool
    /// {
    ///     Name = "GetWeatherForecast",
    ///     Description = "Gets the weather forecast for a location.",
    ///     InputSchema = JsonSerializer.Deserialize&lt;JsonElement&gt;("""
    ///     {
    ///         "type": "object",
    ///         "properties": {
    ///             "latitude": {
    ///                 "type": "number",
    ///                 "description": "Latitude of the location."
    ///             },
    ///             "longitude": {
    ///                 "type": "number",
    ///                 "description": "Longitude of the location."
    ///             }
    ///         },
    ///         "required": ["latitude", "longitude"]
    ///     }
    ///     """)
    /// };
    /// </code>
    /// </example>
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
    /// Optional additional tool information and behavior hints.
    /// These annotations provide metadata about the tool's behavior, such as whether it's read-only,
    /// destructive, idempotent, or operates in an open world. They also can include a human-readable title.
    /// Note that these are hints and should not be relied upon for security decisions.
    /// </summary>
    [JsonPropertyName("annotations")]
    public ToolAnnotations? Annotations { get; set; }
}
