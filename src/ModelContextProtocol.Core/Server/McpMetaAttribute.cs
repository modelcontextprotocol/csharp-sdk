using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Used to specify metadata for an MCP server primitive (tool, prompt, or resource).
/// </summary>
/// <remarks>
/// <para>
/// This attribute can be applied multiple times to a method to specify multiple key/value pairs
/// of metadata. The metadata is used to populate the <see cref="Tool.Meta"/>, <see cref="Prompt.Meta"/>,
/// or <see cref="Resource.Meta"/> property of the corresponding primitive.
/// </para>
/// <para>
/// Metadata can be used to attach additional information to primitives, such as model preferences,
/// version information, or other custom data that should be communicated to MCP clients.
/// </para>
/// <example>
/// <code>
/// [McpServerTool]
/// [McpMeta(Name = "model", Value = "gpt-4o")]
/// [McpMeta(Name = "version", Value = "1.0")]
/// public string MyTool(string input)
/// {
///     return $"Processed: {input}";
/// }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class McpMetaAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpMetaAttribute"/> class.
    /// </summary>
    public McpMetaAttribute()
    {
    }

    /// <summary>
    /// Gets or sets the name (key) of the metadata entry.
    /// </summary>
    /// <remarks>
    /// This value is used as the key in the metadata object. It should be a unique identifier
    /// for this piece of metadata within the context of the primitive.
    /// </remarks>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the value of the metadata entry.
    /// </summary>
    /// <remarks>
    /// This value is stored as a string in the metadata object. Complex values should be
    /// serialized to JSON strings if needed.
    /// </remarks>
    public required string Value { get; set; }
}
