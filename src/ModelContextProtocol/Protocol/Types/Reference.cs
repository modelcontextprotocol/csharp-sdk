using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a reference to a resource or prompt in the Model Context Protocol.
/// This is an umbrella type that combines ResourceReference and PromptReference from the specification schema.
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
/// For more details about the reference schema, see:
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">Model Context Protocol schema</see>
/// </para>
/// </remarks>
/// <example>
/// <para>Creating a resource reference:</para>
/// <code>
/// var resourceRef = new Reference
/// {
///     Type = "ref/resource",
///     Uri = "resource://my-resource/path"
/// };
/// 
/// // Use with CompleteAsync to get completions for a resource
/// var completions = await client.CompleteAsync(
///     resourceRef,
///     argumentName: "param",
///     argumentValue: "value",
///     cancellationToken: ct);
/// </code>
/// 
/// <para>Creating a prompt reference:</para>
/// <code>
/// var promptRef = new Reference
/// {
///     Type = "ref/prompt",
///     Name = "my_prompt_template"
/// };
/// 
/// // Use with CompleteAsync to get completions for a prompt argument
/// var completions = await client.CompleteAsync(
///     promptRef, 
///     argumentName: "style",
///     argumentValue: "fo",
///     cancellationToken: ct);
/// </code>
/// </example>
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
    /// Validates the reference object to ensure it contains the required properties for its type.
    /// </summary>
    /// <param name="validationMessage">When this method returns false, contains a message explaining why validation failed; otherwise, null.</param>
    /// <returns>True if the reference is valid; otherwise, false.</returns>
    /// <remarks>
    /// <para>
    /// For "ref/resource" type, the <see cref="Uri"/> property must not be null or empty.
    /// For "ref/prompt" type, the <see cref="Name"/> property must not be null or empty.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var reference = new Reference
    /// {
    ///     Type = "ref/resource",
    ///     Uri = "resource://my-resource"
    /// };
    /// 
    /// if (!reference.Validate(out string? validationMessage))
    /// {
    ///     Console.WriteLine($"Invalid reference: {validationMessage}");
    /// }
    /// </code>
    /// </example>
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
