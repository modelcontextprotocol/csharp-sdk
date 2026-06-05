using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a tool that the server is capable of calling.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Tool : IBaseMetadata
{
    /// <inheritdoc />
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets a human-readable description of the tool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This description helps the AI model understand what the tool does and when to use it.
    /// It should be clear, concise, and accurately describe the tool's purpose and functionality.
    /// </para>
    /// <para>
    /// The description is typically presented to AI models to help them determine when
    /// and how to use the tool based on user requests. A well-written description significantly
    /// reduces incorrect tool invocations. Include information about what the tool does, any
    /// constraints or prerequisites, and what it returns.
    /// </para>
    /// <para>
    /// Similarly, individual parameter descriptions (provided via <see cref="System.ComponentModel.DescriptionAttribute"/>
    /// on tool method parameters) are important for guiding the model to supply correct argument values.
    /// Descriptions should document expected formats, valid value ranges, and any other constraints
    /// the model should be aware of.
    /// </para>
    /// </remarks>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a JSON Schema object defining the expected parameters for the tool.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a valid MCP tool JSON schema.</exception>
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
    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema
    {
        get => field;
        set
        {
            if (!McpJsonUtilities.IsValidMcpToolSchema(value))
            {
                throw new ArgumentException("The specified document is not a valid MCP tool input JSON schema.", nameof(InputSchema));
            }

            field = value;
        }

    } = McpJsonUtilities.DefaultMcpToolSchema;

    /// <summary>
    /// Gets or sets a JSON Schema object defining the expected structured outputs for the tool.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a valid MCP tool JSON schema.</exception>
    /// <remarks>
    /// <para>
    /// The schema must be a valid JSON Schema object with the "type" property set to "object".
    /// This is enforced by validation in the setter which will throw an <see cref="ArgumentException"/>
    /// if an invalid schema is provided.
    /// </para>
    /// <para>
    /// The schema should describe the shape of the data as returned in <see cref="CallToolResult.StructuredContent"/>.
    /// </para>
    /// </remarks>
    [JsonPropertyName("outputSchema")]
    public JsonElement? OutputSchema
    {
        get => field;
        set
        {
            if (value is not null && !McpJsonUtilities.IsValidMcpToolSchema(value.Value))
            {
                throw new ArgumentException("The specified document is not a valid MCP tool output JSON schema.", nameof(OutputSchema));
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets optional additional tool information and behavior hints.
    /// </summary>
    /// <remarks>
    /// These annotations provide metadata about the tool's behavior, such as whether it's read-only,
    /// destructive, idempotent, or operates in an open world. They also can include a human-readable title.
    /// Note that these are hints and should not be relied upon for security decisions.
    /// </remarks>
    [JsonPropertyName("annotations")]
    public ToolAnnotations? Annotations { get; set; }

    /// <summary>
    /// Gets or sets execution-related metadata for this tool.
    /// </summary>
    /// <remarks>
    /// This property provides hints about how the tool should be executed, particularly
    /// regarding task augmentation support. See <see cref="ToolExecution"/> for details.
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    [JsonIgnore]
    public ToolExecution? Execution
    {
        get => ExecutionCore;
        set => ExecutionCore = value;
    }

    // See ExperimentalInternalPropertyTests.cs before modifying this property.
    [JsonInclude]
    [JsonPropertyName("execution")]
    internal ToolExecution? ExecutionCore { get; set; }

    /// <summary>
    /// Gets or sets an optional list of icons for this tool.
    /// </summary>
    /// <remarks>
    /// This can be used by clients to display the tool's icon in a user interface.
    /// </remarks>
    [JsonPropertyName("icons")]
    public IList<Icon>? Icons { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay
    {
        get
        {
            string desc = Description is not null ? $", Description = \"{Description}\"" : "";
            return $"Name = {Name}{desc}";
        }
    }
}
