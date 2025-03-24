namespace ModelContextProtocol.Server;

/// <summary>
/// Used to mark a public method as an MCP tool.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpServerToolAttribute : Attribute
{
    /// <summary>Gets the name of the tool.</summary>
    /// <remarks>If <see langword="null"/>, the method name will be used.</remarks>
    public string? Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerToolTypeAttribute"/> class.
    /// </summary>
    /// <param name="name">The name of the tool. If <see langword="null"/>, the method name will be used.</param>
    public McpServerToolAttribute(string? name = null) => Name = name;
}
