using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a reference to a resource or prompt. Umbrella type for both ResourceReference and PromptReference from the spec schema.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class Reference
{
    /// <summary>
    /// The type of content. Can be ref/resource or ref/prompt.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The URI or URI template of the resource.
    /// </summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    /// <summary>
    /// The name of the prompt or prompt template.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Returns a string representation of the reference.
    /// </summary>
    public override string ToString()
    {
        return $"\"{Type}\": \"{Uri ?? Name}\"";
    }

    /// <summary>
    /// Validates the reference object.
    /// </summary>
    public bool Validate([NotNullWhen(false)] out string? validationMessage)
    {
        if (Type == "ref/resource")
        {
            if (string.IsNullOrEmpty(Uri))
            {
                validationMessage = "Uri is required for ref/resource";
                return false;
            }
        }
        else if (Type == "ref/prompt")
        {
            if (string.IsNullOrEmpty(Name))
            {
                validationMessage = "Name is required for ref/prompt";
                return false;
            }
        }
        else
        {
            validationMessage = $"Unknown reference type: {Type}";
            return false;
        }

        validationMessage = null;
        return true;
    }
}
