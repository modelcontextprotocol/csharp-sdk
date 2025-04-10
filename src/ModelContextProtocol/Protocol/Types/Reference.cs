using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a reference to a resource or prompt in the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// A Reference object identifies either a resource or a prompt:
/// </para>
/// <list type="bullet">
///   <item><description>For resource references, set <see cref="Type"/> to "ref/resource" and provide the <see cref="Uri"/> property.</description></item>
///   <item><description>For prompt references, set <see cref="Type"/> to "ref/prompt" and provide the <see cref="Name"/> property.</description></item>
/// </list>
/// <para>
/// References are commonly used with <see cref="McpClientExtensions.CompleteAsync"/> to request completion suggestions for arguments,
/// and with other methods that need to reference resources or prompts.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public class Reference
{
    /// <summary>
    /// Gets or sets the type of content.
    /// </summary>
    /// <remarks>
    /// This can be "ref/resource" or "ref/prompt".
    /// </remarks>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URI or URI template of the resource.
    /// </summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    /// <summary>
    /// Gets or sets the name of the prompt or prompt template.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"\"{Type}\": \"{Uri ?? Name}\"";

    /// <summary>
    /// Validates the reference object to ensure it contains the required properties for its type.
    /// </summary>
    /// <param name="validationMessage">When this method returns false, contains a message explaining why validation failed; otherwise, null.</param>
    /// <returns>True if the reference is valid; otherwise, false.</returns>
    /// <remarks>
    /// For "ref/resource" type, the <see cref="Uri"/> property must not be null or empty.
    /// For "ref/prompt" type, the <see cref="Name"/> property must not be null or empty.
    /// </remarks>
    public bool Validate([NotNullWhen(false)] out string? validationMessage)
    {
        switch (Type)
        {
            case "ref/resource":
                if (string.IsNullOrEmpty(Uri))
                {
                    validationMessage = "Uri is required for ref/resource";
                    return false;
                }
                break;

            case "ref/prompt":
                if (string.IsNullOrEmpty(Name))
                {
                    validationMessage = "Name is required for ref/prompt";
                    return false;
                }
                break;

            default:
                validationMessage = $"Unknown reference type: {Type}";
                return false;
        }

        validationMessage = null;
        return true;
    }
}
