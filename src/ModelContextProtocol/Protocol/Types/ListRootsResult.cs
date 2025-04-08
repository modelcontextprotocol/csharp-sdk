namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The client's response to a roots/list request from the server.
/// </summary>
/// <remarks>
/// This class represents the response returned by a client when the server requests a list of available root URIs.
/// Root URIs are entry points for resource navigation in the Model Context Protocol.
/// The response contains a collection of <see cref="Root"/> objects, each describing a root URI and its metadata.
/// </remarks>
/// <example>
/// <code>
/// // Create a response with available roots
/// var result = new ListRootsResult
/// {
///     Roots = new List&lt;Root&gt;
///     {
///         new Root { Uri = "mcp://mymodel/", Name = "My Model" },
///         new Root { Uri = "mcp://another-model/", Name = "Another Model" }
///     }
/// };
/// 
/// // Server code accessing the result returned by a client
/// foreach (var root in result.Roots)
/// {
///     Console.WriteLine($"Root URI: {root.Uri}, Name: {root.Name}");
/// }
/// </code>
/// </example>
/// <seealso cref="ListRootsRequestParams"/>
/// <seealso cref="Root"/>
/// <seealso cref="RootsCapability"/>
/// <seealso cref="RequestMethods.RootsList"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">Model Context Protocol schema for details</seealso>
public class ListRootsResult
{
    /// <summary>
    /// Additional metadata for the result. Reserved by the protocol for future use.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("meta")]
    public object? Meta { get; init; }

    /// <summary>
    /// The list of root URIs provided by the client.
    /// </summary>
    /// <remarks>
    /// This collection contains all available root URIs and their associated metadata.
    /// Each root serves as an entry point for resource navigation in the Model Context Protocol.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Accessing roots from a client response
    /// foreach (var root in result.Roots)
    /// {
    ///     Console.WriteLine($"Root URI: {root.Uri}, Name: {root.Name}");
    ///     // Process or display each root
    /// }
    /// </code>
    /// </example>
    [System.Text.Json.Serialization.JsonPropertyName("roots")]
    public required IReadOnlyList<Root> Roots { get; init; }
}
