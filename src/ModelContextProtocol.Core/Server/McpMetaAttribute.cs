using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <summary>
/// Used to specify metadata for an MCP server primitive (tool, prompt, or resource).
/// </summary>
/// <remarks>
/// <para>
/// The metadata is used to populate the <see cref="Tool.Meta"/>, <see cref="Prompt.Meta"/>,
/// or <see cref="Resource.Meta"/> property of the corresponding primitive.
/// </para>
/// <para>
/// This attribute can be applied multiple times to a method to specify multiple key/value pairs
/// of metadata. However, the same key should not be used more than once; doing so will result
/// in undefined behavior.
/// </para>
/// <para>
/// Metadata can be used to attach additional information to primitives, such as model preferences,
/// version information, or other custom data that should be communicated to MCP clients.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [McpServerTool]
/// [McpMeta("model", "gpt-4o")]
/// [McpMeta("version", "1.0")]
/// [McpMeta("priority", 5.0)]
/// [McpMeta("isBeta", true)]
/// [McpMeta("tags", JsonValue = """["a","b"]""")]
/// public string MyTool(string input) => $"Processed: {input}";
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class McpMetaAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpMetaAttribute"/> class with a string value.
    /// </summary>
    /// <param name="name">The name (key) of the metadata entry.</param>
    /// <param name="value">The string value of the metadata entry. If null, the value will be serialized as JSON null.</param>
    public McpMetaAttribute(string name, string? value = null)
    {
        Name = name;
        JsonValue = value is null ? "null" : JsonSerializer.Serialize(value, McpJsonUtilities.JsonContext.Default.String);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpMetaAttribute"/> class with a double value.
    /// </summary>
    /// <param name="name">The name (key) of the metadata entry.</param>
    /// <param name="value">The double value of the metadata entry.</param>
    public McpMetaAttribute(string name, double value)
    {
        Name = name;
        JsonValue = JsonSerializer.Serialize(value, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(double)));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpMetaAttribute"/> class with a boolean value.
    /// </summary>
    /// <param name="name">The name (key) of the metadata entry.</param>
    /// <param name="value">The boolean value of the metadata entry.</param>
    public McpMetaAttribute(string name, bool value)
    {
        Name = name;
        JsonValue = JsonSerializer.Serialize(value, McpJsonUtilities.JsonContext.Default.Boolean);
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
    /// Gets or sets the value of the metadata entry as a JSON string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value must be well-formed JSON. It will be parsed and added to the metadata <see cref="JsonObject"/>.
    /// Simple values can be represented as JSON literals like <c>"\"my-string\""</c>, <c>"123"</c>, 
    /// <c>"true"</c>, etc. Complex structures can be represented as JSON objects or arrays.
    /// </para>
    /// <para>
    /// Setting this property will override any value provided via the constructor.
    /// </para>
    /// <para>
    /// For programmatic scenarios where you want to construct complex metadata without dealing with
    /// JSON strings, use the <see cref="McpServerToolCreateOptions.Meta"/>, 
    /// <see cref="McpServerPromptCreateOptions.Meta"/>, or <see cref="McpServerResourceCreateOptions.Meta"/> 
    /// property to provide a JsonObject directly.
    /// </para>
    /// </remarks>
    [StringSyntax(StringSyntaxAttribute.Json)]
    public string JsonValue { get; set; }
}
