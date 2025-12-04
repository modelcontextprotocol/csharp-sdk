using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>Provides a base interface for metadata with name (identifier) and title (display name) properties.</summary>
public interface IBaseMetadata
{
    /// <summary>
    /// Gets or sets the unique identifier for this item.
    /// </summary>
    [JsonPropertyName("name")]
    string Name { get; set; }

    /// <summary>
    /// Gets or sets a title.
    /// </summary>
    /// <remarks>
    /// This is intended for UI and end-user contexts. It is optimized to be human-readable and easily understood,
    /// even by those unfamiliar with domain-specific terminology.
    /// If not provided, <see cref="Name"/> can be used for display (except for tools, where <see cref="ToolAnnotations.Title"/>, if present,
    /// should be given precedence over using <see cref="Name"/>).
    /// </remarks>
    [JsonPropertyName("title")]
    string? Title { get; set; }
}
