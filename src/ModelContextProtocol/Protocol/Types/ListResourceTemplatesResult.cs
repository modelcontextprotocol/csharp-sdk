using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a resources/templates/list request from the client, containing available resource templates and pagination information.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <remarks>
/// <para>
/// This result is returned when a client sends a resources/templates/list request to discover available resource templates on the server.
/// It inherits from <see cref="PaginatedResult"/>, allowing for paginated responses when there are many resource templates.
/// </para>
/// <para>
/// When the number of resource templates is large, the server can use the <see cref="PaginatedResult.NextCursor"/> property
/// to indicate more templates are available beyond what was returned in the current response. Clients can use this cursor
/// in subsequent requests to retrieve additional pages of resource templates.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Client-side pagination example
/// var result = await client.ListResourceTemplatesAsync();
/// List&lt;ResourceTemplate&gt; allTemplates = new(result.ResourceTemplates);
/// 
/// // Continue fetching while there are more pages
/// while (result.NextCursor != null)
/// {
///     result = await client.ListResourceTemplatesAsync(new() { Cursor = result.NextCursor });
///     allTemplates.AddRange(result.ResourceTemplates);
/// }
/// </code>
/// </example>
/// <seealso cref="PaginatedResult"/>
/// <seealso cref="ResourceTemplate"/>
public class ListResourceTemplatesResult : PaginatedResult
{
    /// <summary>
    /// A list of resource templates that the server offers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This collection contains all the resource templates returned in the current page of results.
    /// Each <see cref="ResourceTemplate"/> provides metadata about resources available on the server,
    /// including URI templates, names, descriptions, and MIME types.
    /// </para>
    /// <para>
    /// Clients can use these templates to discover what resources are available and how to
    /// construct URIs to access them. When the server has more templates than returned in a single response,
    /// check the <see cref="PaginatedResult.NextCursor"/> property for additional pages.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Access resource templates from a response
    /// var result = await client.ListResourceTemplatesAsync();
    /// foreach (var template in result.ResourceTemplates)
    /// {
    ///     Console.WriteLine($"Found template: {template.Name}");
    ///     Console.WriteLine($"  URI pattern: {template.UriTemplate}");
    ///     if (template.Description != null)
    ///         Console.WriteLine($"  Description: {template.Description}");
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="ResourceTemplate"/>
    /// <seealso cref="PaginatedResult.NextCursor"/>
    [System.Text.Json.Serialization.JsonPropertyName("resourceTemplates")]
    public List<ResourceTemplate> ResourceTemplates { get; set; } = [];
}