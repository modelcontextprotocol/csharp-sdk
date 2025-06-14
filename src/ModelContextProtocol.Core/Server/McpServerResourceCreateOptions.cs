using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides options for controlling the creation of an <see cref="McpServerResource"/>.
/// </summary>
/// <remarks>
/// <para>
/// These options allow for customizing the behavior and metadata of resources created with
/// <see cref="M:McpServerResource.Create"/>. They provide control over naming, description,
/// and dependency injection integration.
/// </para>
/// <para>
/// When creating resources programmatically rather than using attributes, these options
/// provide the same level of configuration flexibility.
/// </para>
/// </remarks>
public sealed class McpServerResourceCreateOptions
{
    /// <summary>
    /// Gets or sets optional services used in the construction of the <see cref="McpServerResource"/>.
    /// </summary>
    /// <remarks>
    /// These services will be used to determine which parameters should be satisifed from dependency injection. As such,
    /// what services are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </remarks>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// Gets or sets the URI template of the <see cref="McpServerResource"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but an <see cref="McpServerResourceAttribute"/> is applied to the member,
    /// the <see cref="McpServerResourceAttribute.UriTemplate"/> from the attribute will be used. If that's not present,
    /// a URI template will be inferred from the member's signature.
    /// </remarks>
    public string? UriTemplate { get; set; }

    /// <summary>
    /// Gets or sets the name to use for the <see cref="McpServerResource"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but an <see cref="McpServerResourceAttribute"/> is applied to the member,
    /// the name from the attribute will be used. If that's not present, a name based on the members's name will be used.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the title to use for the <see cref="McpServerPrompt"/>.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or set the description to use for the <see cref="McpServerResource"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but a <see cref="DescriptionAttribute"/> is applied to the member,
    /// the description from that attribute will be used.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the MIME (media) type of the <see cref="McpServerResource"/>.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the JSON serializer options to use when marshalling data to/from JSON.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="McpJsonUtilities.DefaultOptions"/> if left unspecified.
    /// </remarks>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets the JSON schema options when creating <see cref="AIFunction"/> from a method.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="AIJsonSchemaCreateOptions.Default"/> if left unspecified.
    /// </remarks>
    public AIJsonSchemaCreateOptions? SchemaCreateOptions { get; set; }

    /// <summary>
    /// Creates a shallow clone of the current <see cref="McpServerResourceCreateOptions"/> instance.
    /// </summary>
    internal McpServerResourceCreateOptions Clone() =>
        new McpServerResourceCreateOptions
        {
            Services = Services,
            UriTemplate = UriTemplate,
            Name = Name,
            Title = Title,
            Description = Description,
            MimeType = MimeType,
            SerializerOptions = SerializerOptions,
            SchemaCreateOptions = SchemaCreateOptions,
        };
}
