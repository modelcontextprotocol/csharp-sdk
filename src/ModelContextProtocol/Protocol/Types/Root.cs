namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a root URI and its metadata.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class Root
{
    /// <summary>
    /// The URI of the root.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>
    /// A human-readable name for the root.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Additional metadata for the root. Reserved by the protocol for future use.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("meta")]
    public object? Meta { get; init; }
}