namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a root URI and its metadata in the Model Context Protocol.
/// Root URIs serve as entry points for resource navigation, typically representing
/// top-level directories or container resources that can be accessed and traversed.
/// </summary>
/// <remarks>
/// Roots provide a hierarchical structure for organizing and accessing resources within the protocol.
/// Each root has a URI that uniquely identifies it and optional metadata like a human-readable name.
/// </remarks>
/// <example>
/// <code>
/// // Creating a root
/// var root = new Root { 
///     Uri = "mcp://mymodel/", 
///     Name = "My Model" 
/// };
/// 
/// // Using roots in a ListRootsResult
/// var result = new ListRootsResult {
///     Roots = new List&lt;Root&gt; {
///         new Root { Uri = "mcp://mymodel/", Name = "My Model" },
///         new Root { Uri = "mcp://another-model/", Name = "Another Model" }
///     }
/// };
/// </code>
/// </example>
/// <seealso cref="ListRootsRequestParams"/>
/// <seealso cref="ListRootsResult"/>
/// <seealso cref="RootsCapability"/>
/// <seealso cref="RequestMethods.RootsList"/>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
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