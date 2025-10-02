namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an MCP server primitive, like a tool or a prompt.
/// </summary>
public interface IMcpServerPrimitive
{
    /// <summary>Gets the unique identifier of the primitive.</summary>
    string Id { get; }

    /// <summary>
    /// Gets the metadata for this primitive instance.
    /// </summary>
    /// <remarks>
    /// Contains attributes from the associated MethodInfo and declaring class (if any),
    /// with class-level attributes appearing before method-level attributes.
    /// </remarks>
    IReadOnlyList<object> Metadata { get; }
}
