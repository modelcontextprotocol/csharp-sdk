namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Describes an argument that a prompt can accept for templating and customization.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="PromptArgument"/> class defines metadata for arguments that can be provided
/// to a prompt. These arguments are used to customize or parameterize prompts when they are 
/// retrieved using <c>GetPrompt</c> requests.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// // Define a prompt with arguments
/// var prompt = new Prompt
/// {
///     Name = "greeting",
///     Template = "Hello, {{name}}! Welcome to {{service}}.",
///     Arguments = new List&lt;PromptArgument&gt;
///     {
///         new PromptArgument
///         {
///             Name = "name",
///             Description = "The user's name",
///             Required = true
///         },
///         new PromptArgument
///         {
///             Name = "service",
///             Description = "The service name",
///             Required = true
///         }
///     }
/// };
/// </code>
/// </para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </remarks>
public class PromptArgument
{
    /// <summary>
    /// The name of the argument used for referencing in prompt templates.
    /// </summary>
    /// <remarks>
    /// This name is typically referenced in the prompt template using a pattern like <c>{{name}}</c>
    /// to indicate where the argument value should be inserted.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A human-readable description of the argument's purpose and expected values.
    /// </summary>
    /// <remarks>
    /// This description helps developers understand what information should be provided
    /// for this argument and how it will affect the generated prompt.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether this argument must be provided when requesting the prompt.
    /// </summary>
    /// <remarks>
    /// When set to <c>true</c>, the client must include this argument when making a <c>GetPrompt</c> request.
    /// If a required argument is missing, the server should respond with an error.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("required")]
    public bool? Required { get; set; }
}