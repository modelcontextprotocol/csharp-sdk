using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents an argument that a prompt can accept for templating and customization.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="PromptArgument"/> class defines metadata for arguments that can be provided
/// to a prompt. These arguments are used to customize or parameterize prompts when they are
/// retrieved using <see cref="RequestMethods.PromptsGet"/> requests.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class PromptArgument : IBaseMetadata
{
    /// <inheritdoc />
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets a human-readable description of the argument's purpose and expected values.
    /// </summary>
    /// <remarks>
    /// This description helps developers understand what information should be provided
    /// for this argument and how it will affect the generated prompt.
    /// </remarks>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether this argument must be provided when requesting the prompt.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, the client must include this argument when making a <see cref="RequestMethods.PromptsGet"/> request.
    /// If a required argument is missing, the server should respond with an error.
    /// </remarks>
    [JsonPropertyName("required")]
    public bool? Required { get; set; }
}
