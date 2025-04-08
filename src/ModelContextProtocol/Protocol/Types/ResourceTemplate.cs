using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a known resource template that the server is capable of reading.
/// Resource templates provide metadata about resources available on the server,
/// including how to construct URIs for those resources.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <remarks>
/// Resource templates are typically returned by the server in response to a ListResourceTemplates request.
/// They provide information that helps clients understand what resources are available and how to access them.
/// </remarks>
/// <example>
/// <code>
/// // Server-side: Implementing a handler that returns available resource templates
/// .WithListResourceTemplatesHandler((ctx, ct) =>
/// {
///     return Task.FromResult(new ListResourceTemplatesResult
///     {
///         ResourceTemplates =
///         [
///             new ResourceTemplate { 
///                 Name = "Static Resource", 
///                 Description = "A static resource with a numeric ID", 
///                 UriTemplate = "test://static/resource/{id}" 
///             }
///         ]
///     });
/// })
/// 
/// // Client-side: Retrieving available resource templates
/// IList&lt;ResourceTemplate&gt; templates = await client.ListResourceTemplatesAsync(cancellationToken);
/// foreach (var template in templates)
/// {
///     Console.WriteLine($"Name: {template.Name}, URI Template: {template.UriTemplate}");
/// }
/// </code>
/// </example>
public record ResourceTemplate
{
    /// <summary>
    /// The URI template (according to RFC 6570) that can be used to construct resource URIs.
    /// </summary>
    [JsonPropertyName("uriTemplate")]
    public required string UriTemplate { get; init; }

    /// <summary>
    /// A human-readable name for this resource template.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// A description of what this resource template represents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This description helps clients understand the purpose and content of resources
    /// that can be generated from this template. It can be used by client applications
    /// to provide context about available resource types or to display in user interfaces.
    /// </para>
    /// <para>
    /// For AI models, this description can serve as a hint about when and how to use
    /// the resource template, enhancing the model's ability to generate appropriate URIs.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var template = new ResourceTemplate
    /// {
    ///     Name = "Document",
    ///     Description = "A document resource containing formatted text content",
    ///     UriTemplate = "documents/{id}"
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// The MIME type of this resource template, if known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Specifies the expected format of resources that can be generated from this template.
    /// This helps clients understand what type of content to expect when accessing resources
    /// created using this template.
    /// </para>
    /// <para>
    /// Common MIME types include "text/plain" for plain text, "application/pdf" for PDF documents,
    /// "image/png" for PNG images, "application/json" for JSON data, etc.
    /// </para>
    /// <para>
    /// This property may be null if the MIME type is unknown, variable depending on the specific resource,
    /// or not applicable for this type of resource template.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var docTemplate = new ResourceTemplate
    /// {
    ///     UriTemplate = "documents/{documentId}",
    ///     Name = "Document Template",
    ///     Description = "Template for accessing formatted documents",
    ///     MimeType = "application/pdf"
    /// };
    /// 
    /// var imageTemplate = new ResourceTemplate
    /// {
    ///     UriTemplate = "images/{imageId}",
    ///     Name = "Image Template",
    ///     Description = "Template for accessing image resources",
    ///     MimeType = "image/png"
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    /// <summary>
    /// Optional annotations for the resource template.
    /// These annotations can be used to specify the intended audience (User, Assistant, or both)
    /// and the priority level of the resource template. Clients can use this information to filter
    /// or prioritize resource templates for different roles.
    /// </summary>
    [JsonPropertyName("annotations")]
    public Annotations? Annotations { get; init; }
}