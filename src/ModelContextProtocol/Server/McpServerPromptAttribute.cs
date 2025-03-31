namespace ModelContextProtocol.Server;

/// <summary>
/// Used to indicate that a method should be considered an MCP prompt and describe it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpServerPromptAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerPromptAttribute"/> class.
    /// </summary>
    public McpServerPromptAttribute()
    {
    }

    /// <summary>Gets the name of the prompt.</summary>
    /// <remarks>If <see langword="null"/>, the method name will be used.</remarks>
    public string? Name { get; set; }
}
