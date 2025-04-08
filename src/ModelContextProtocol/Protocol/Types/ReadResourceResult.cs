namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a resources/read request from the client.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ReadResourceResult
{
    /// <summary>
    /// A list of <see cref="ResourceContents"/> objects that this resource contains.
    /// </summary>
    /// <remarks>
    /// This property contains the actual content of the requested resource, which can be
    /// either text-based (<see cref="TextResourceContents"/>) or binary (<see cref="BlobResourceContents"/>).
    /// The type of content included depends on the resource being accessed.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Accessing contents from a read resource response
    /// ReadResourceResult result = await client.ReadResourceAsync("resource://document");
    /// 
    /// // Check if there are any contents
    /// if (result.Contents.Count > 0)
    /// {
    ///     // Process text resource
    ///     if (result.Contents[0] is TextResourceContents textContent)
    ///     {
    ///         Console.WriteLine($"Text resource: {textContent.Text}");
    ///     }
    ///     // Process binary resource
    ///     else if (result.Contents[0] is BlobResourceContents blobContent)
    ///     {
    ///         byte[] data = Convert.FromBase64String(blobContent.Blob);
    ///         // Process the binary data
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="ResourceContents"/>
    /// <seealso cref="TextResourceContents"/>
    /// <seealso cref="BlobResourceContents"/>
    [System.Text.Json.Serialization.JsonPropertyName("contents")]
    public List<ResourceContents> Contents { get; set; } = [];
}
