namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// A prompt or prompt template that the server offers.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class Prompt
{
    /// <summary>
    /// A list of arguments that this prompt accepts for templating and customization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This list defines the arguments that can be provided when requesting the prompt.
    /// Each argument specifies metadata like name, description, and whether it's required.
    /// </para>
    /// <para>
    /// When a client makes a GetPrompt request, it can provide values for these arguments
    /// which will be substituted into the prompt template.
    /// </para>
    /// 
    /// <example>
    /// <code>
    /// // Defining a prompt with template arguments
    /// var prompt = new Prompt
    /// {
    ///     Name = "greeting",
    ///     Description = "A customizable greeting template",
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
    ///             Description = "The name of the service",
    ///             Required = true
    ///         }
    ///     }
    /// };
    /// </code>
    /// </example>
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("arguments")]
    public List<PromptArgument>? Arguments { get; set; }

    /// <summary>
    /// An optional description of what this prompt provides
    /// </summary>
    /// <remarks>
    /// <para>
    /// This description helps developers understand the purpose and use cases for the prompt.
    /// It should explain what the prompt is designed to accomplish and any important context.
    /// </para>
    /// <para>
    /// The description is typically used in documentation, UI displays, and for providing context
    /// to client applications that may need to choose between multiple available prompts.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var prompt = new Prompt
    /// {
    ///     Name = "greeting",
    ///     Description = "A customizable greeting template for welcoming users",
    ///     Arguments = new List&lt;PromptArgument&gt;
    ///     {
    ///         new() { Name = "name", Description = "The user's name", Required = true }
    ///     }
    /// };
    /// </code>
    /// </example>
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The name of the prompt or prompt template.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
