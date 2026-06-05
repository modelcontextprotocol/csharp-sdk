using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a known resource template that the server is capable of reading.
/// </summary>
/// <remarks>
/// Resource templates provide metadata about resources available on the server,
/// including how to construct URIs for those resources.
/// </remarks>
public sealed class ResourceTemplate : IBaseMetadata
{
    /// <inheritdoc />
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the URI template (according to RFC 6570) that can be used to construct resource URIs.
    /// </summary>
    [JsonPropertyName("uriTemplate")]
    public required string UriTemplate { get; set; }

    /// <summary>
    /// Gets or sets a description of what this resource template represents.
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
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of this resource template, if known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Specifies the expected format of resources that can be generated from this template.
    /// This helps clients understand what type of content to expect when accessing resources
    /// created using this template.
    /// </para>
    /// <para>
    /// Common MIME types include "text/plain" for plain text, "application/pdf" for PDF documents,
    /// "image/png" for PNG images, or "application/json" for JSON data.
    /// </para>
    /// </remarks>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets optional annotations for the resource template.
    /// </summary>
    /// <remarks>
    /// These annotations can be used to specify the intended audience (<see cref="Role.User"/>, <see cref="Role.Assistant"/>, or both)
    /// and the priority level of the resource template. Clients can use this information to filter
    /// or prioritize resource templates for different roles.
    /// </remarks>
    [JsonPropertyName("annotations")]
    public Annotations? Annotations { get; set; }

    /// <summary>
    /// Gets or sets an optional list of icons for this resource template.
    /// </summary>
    /// <remarks>
    /// This value can be used by clients to display the resource template's icon in a user interface.
    /// </remarks>
    [JsonPropertyName("icons")]
    public IList<Icon>? Icons { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }

    /// <summary>Gets a value that indicates whether <see cref="UriTemplate"/> contains any template expressions.</summary>
    [JsonIgnore]
    public bool IsTemplated => UriTemplate.Contains('{');

    /// <summary>Converts the <see cref="ResourceTemplate"/> into a <see cref="Resource"/>.</summary>
    /// <returns>A <see cref="Resource"/> if <see cref="IsTemplated"/> is <see langword="false"/>; otherwise, <see langword="null"/>.</returns>
    public Resource? AsResource()
    {
        if (IsTemplated)
        {
            return null;
        }

        return new()
        {
            Uri = UriTemplate,
            Name = Name,
            Title = Title,
            Description = Description,
            MimeType = MimeType,
            Annotations = Annotations,
            Icons = Icons,
            Meta = Meta,
        };
    }
}
