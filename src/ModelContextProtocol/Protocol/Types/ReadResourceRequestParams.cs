namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the client to the server, to read a specific resource URI.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <remarks>
/// This class represents the parameters sent in a resource read request. The server uses these
/// parameters to identify and retrieve the requested resource's contents. The primary parameter
/// is the resource URI, which can use any protocol or scheme supported by the server.
/// </remarks>
/// <example>
/// <code>
/// // Client-side usage with IMcpClient
/// var client = await _connector.ConnectAsync("client-id");
/// var result = await client.ReadResourceAsync("resource://documents/12345");
/// 
/// // Server-side handler implementation
/// .WithReadResourceHandler((context, cancellationToken) => 
/// {
///     var uri = context.Params?.Uri;
///     if (string.IsNullOrEmpty(uri))
///     {
///         throw new McpException("Resource URI is required");
///     }
///     
///     // Process the resource URI and return appropriate content
///     return Task.FromResult(new ReadResourceResult
///     {
///         Contents = 
///         [
///             new TextResourceContents
///             {
///                 Uri = uri,
///                 MimeType = "text/plain",
///                 Text = "Resource content here"
///             }
///         ]
///     });
/// })
/// </code>
/// </example>
public class ReadResourceRequestParams : RequestParams
{
    /// <summary>
    /// The URI of the resource to read. The URI can use any protocol; it is up to the server how to interpret it.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("uri")]
    public string? Uri { get; init; }
}
