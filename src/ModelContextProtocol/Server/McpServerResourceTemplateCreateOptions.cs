using System.ComponentModel;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides options for controlling the creation of an <see cref="McpServerResourceTemplate"/>.
/// </summary>
/// <remarks>
/// <para>
/// These options allow for customizing the behavior and metadata of resources created with
/// <see cref="M:McpServerResourceTemplate.Create"/>. They provide control over naming, description,
/// and dependency injection integration.
/// </para>
/// <para>
/// When creating resources programmatically rather than using attributes, these options
/// provide the same level of configuration flexibility.
/// </para>
/// </remarks>
public sealed class McpServerResourceTemplateCreateOptions
{
    /// <summary>
    /// Gets or sets optional services used in the construction of the <see cref="McpServerResourceTemplate"/>.
    /// </summary>
    /// <remarks>
    /// These services will be used to determine which parameters should be satisifed from dependency injection. As such,
    /// what services are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </remarks>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// Gets or sets the URI template of the <see cref="McpServerResourceTemplate"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but an <see cref="McpServerResourceAttribute"/> is applied to the member,
    /// the <see cref="McpServerResourceAttribute.Uri"/> from the attribute will be used. If that's not present,
    /// constructing the resource will fail.
    /// </remarks>
    public string? UriTemplate { get; set; }

    /// <summary>
    /// Gets or sets the name to use for the <see cref="McpServerResourceTemplate"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but an <see cref="McpServerResourceAttribute"/> is applied to the member,
    /// the name from the attribute will be used. If that's not present, a name based on the members's name will be used.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or set the description to use for the <see cref="McpServerResourceTemplate"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but a <see cref="DescriptionAttribute"/> is applied to the member,
    /// the description from that attribute will be used.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the MIME (media) type of the <see cref="McpServerResourceTemplate"/>.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Creates a shallow clone of the current <see cref="McpServerResourceTemplateCreateOptions"/> instance.
    /// </summary>
    internal McpServerResourceTemplateCreateOptions Clone() =>
        new McpServerResourceTemplateCreateOptions
        {
            Services = Services,
            UriTemplate = UriTemplate,
            Name = Name,
            Description = Description,
            MimeType = MimeType,
        };
}
