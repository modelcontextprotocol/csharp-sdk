using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a root URI and its metadata in the Model Context Protocol.
/// </summary>
/// <remarks>
/// Root URIs serve as entry points for resource navigation, typically representing
/// top-level directories or container resources that are relevant to the current session.
/// Roots inform servers which locations the client considers important, providing informational
/// guidance rather than an access-control mechanism. Each root has a URI that uniquely identifies
/// it and optional metadata like a human-readable name.
/// </remarks>
public sealed class Root
{
    /// <summary>
    /// Gets or sets the URI of the root.
    /// </summary>
    [JsonPropertyName("uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public required string Uri { get; set; }

    /// <summary>
    /// Gets or sets a human-readable name for the root.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets additional metadata for the root.
    /// </summary>
    /// <remarks>
    /// This is reserved by the protocol for future use.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }
}
