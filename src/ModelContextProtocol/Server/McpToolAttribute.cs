namespace ModelContextProtocol.Server;

/// <summary>
/// Attribute to mark a method as an MCP tool.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpToolAttribute : Attribute
{
    /// <summary>Gets the name of the tool.</summary>
    /// <remarks>If not provided, the method name will be used.</remarks>
    public string? Name { get; }
    /// <summary>
    /// Attribute to mark a method as an MCP tool.
    /// </summary>
    /// <param name="name">The name of the tool. If not provided, the method name will be used.</param>
    public McpToolAttribute(string? name = null)
    {
        Name = name;
    }
}
