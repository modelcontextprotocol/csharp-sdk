using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides options for controlling the creation of an <see cref="McpServerPrompt"/>.
/// </summary>
/// <remarks>
/// <para>
/// These options allow for customizing the behavior and metadata of prompts created with
/// <see cref="M:McpServerPrompt.Create"/>. They provide control over naming, description,
/// and dependency injection integration.
/// </para>
/// <para>
/// When creating prompts programmatically rather than using attributes, these options
/// provide the same level of configuration flexibility.
/// </para>
/// </remarks>
public sealed class McpServerPromptCreateOptions
{
    /// <summary>
    /// Gets or sets optional services used in the construction of the <see cref="McpServerPrompt"/>.
    /// </summary>
    /// <remarks>
    /// These services will be used to determine which parameters should be satisfied from dependency injection. As such,
    /// what services are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </remarks>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// Gets or sets the name to use for the <see cref="McpServerPrompt"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but an <see cref="McpServerPromptAttribute"/> is applied to the method,
    /// the name from the attribute is used. If that's not present, a name based on the method's name is used.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the title to use for the <see cref="McpServerPrompt"/>.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the description to use for the <see cref="McpServerPrompt"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but a <see cref="DescriptionAttribute"/> is applied to the method,
    /// the description from that attribute is used.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the JSON serializer options to use when marshalling data to/from JSON.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="McpJsonUtilities.DefaultOptions"/> if left unspecified.
    /// </remarks>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets the JSON schema options when creating an <see cref="AIFunction"/> from a method.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="AIJsonSchemaCreateOptions.Default"/> if left unspecified.
    /// </remarks>
    public AIJsonSchemaCreateOptions? SchemaCreateOptions { get; set; }

    /// <summary>
    /// Gets or sets the metadata associated with the prompt.
    /// </summary>
    /// <remarks>
    /// Metadata includes information such as the attributes extracted from the method and its declaring class.
    /// If not provided, metadata will be automatically generated for methods created via reflection.
    /// </remarks>
    public IReadOnlyList<object>? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the icons for this prompt.
    /// </summary>
    /// <remarks>
    /// This property can be used by clients to display the prompt's icon in a user interface.
    /// </remarks>
    public IList<Icon>? Icons { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This <see cref="JsonObject"/> is used to seed the <see cref="Prompt.Meta"/> property. Any metadata from
    /// <see cref="McpMetaAttribute"/> instances on the method will be added to this object, but
    /// properties already present in this <see cref="JsonObject"/> will not be overwritten.
    /// </para>
    /// <para>
    /// Implementations must not make assumptions about its contents.
    /// </para>
    /// </remarks>
    public JsonObject? Meta { get; set; }

    /// <summary>
    /// Creates a shallow clone of the current <see cref="McpServerPromptCreateOptions"/> instance.
    /// </summary>
    internal McpServerPromptCreateOptions Clone() =>
        new()
        {
            Services = Services,
            Name = Name,
            Title = Title,
            Description = Description,
            SerializerOptions = SerializerOptions,
            SchemaCreateOptions = SchemaCreateOptions,
            Metadata = Metadata,
            Icons = Icons,
            Meta = Meta,
        };
}
