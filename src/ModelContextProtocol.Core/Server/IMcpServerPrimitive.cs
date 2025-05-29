namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an MCP server primitive, like a tool or a prompt.
/// </summary>
public interface IMcpServerPrimitive
{
    /// <summary>Gets the unique identifier of the primitive.</summary>
    string Id { get; }
}
