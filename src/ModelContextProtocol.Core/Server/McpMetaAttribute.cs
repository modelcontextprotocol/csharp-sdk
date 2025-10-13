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
/// [McpMeta("model", "gpt-4o")]
/// [McpMeta("version", "1.0")]
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
    /// <param name="name">The name (key) of the metadata entry.</param>
    /// <param name="value">The value of the metadata entry. This can be any value that can be encoded in .NET metadata.</param>
    public McpMetaAttribute(string name, object? value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>
    /// Gets the name (key) of the metadata entry.
    /// </summary>
    /// <remarks>
    /// This value is used as the key in the metadata object. It should be a unique identifier
    /// for this piece of metadata within the context of the primitive.
    /// </remarks>
    public string Name { get; }

    /// <summary>
    /// Gets the value of the metadata entry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value can be any object that can be encoded in .NET metadata (strings, numbers, booleans, etc.).
    /// The value will be serialized to JSON using <see cref="System.Text.Json.JsonSerializer"/> when
    /// populating the metadata JsonObject.
    /// </para>
    /// <para>
    /// For complex JSON structures that cannot be represented as .NET metadata, use the
    /// <see cref="McpServerToolCreateOptions.Meta"/>, <see cref="McpServerPromptCreateOptions.Meta"/>, 
    /// or <see cref="McpServerResourceCreateOptions.Meta"/> property to provide a JsonObject directly.
    /// </para>
    /// </remarks>
    public object? Value { get; }
}
