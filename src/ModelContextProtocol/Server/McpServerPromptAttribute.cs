namespace ModelContextProtocol.Server;

/// <summary>
/// Used to indicate that a method should be considered an MCP prompt and describe it.
/// </summary>
/// <remarks>
/// <para>
/// This attribute marks methods that should be exposed as Model Context Protocol (MCP) prompts. 
/// When used with discovery methods like <c>WithPrompts&lt;T&gt;</c> or <c>WithPromptsFromAssembly</c>,
/// it enables automatic registration of prompt methods without explicitly registering each one.
/// </para>
/// <para>
/// Typically used within classes that are marked with <see cref="McpServerPromptTypeAttribute"/>, 
/// this attribute can be applied to both instance and static methods. For instance methods, a new
/// instance of the containing class will be constructed for each invocation of the prompt.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// [McpServerPromptType]
/// public class CustomerServicePrompts
/// {
///     [McpServerPrompt(Name = "greetingPrompt")]
///     public static string GetGreeting()
///     {
///         return "Hello! How can I assist you today?";
///     }
///     
///     [McpServerPrompt] // Name defaults to method name "GetHelpPrompt"
///     public static string GetHelpPrompt(string topic)
///     {
///         return $"I'll help you with your {topic} issue. Could you provide more details?";
///     }
/// }
/// </code>
/// </para>
/// <para>
/// Registration in a dependency injection container:
/// <code>
/// // Register a specific prompt class
/// builder.Services.AddMcpServer()
///     .WithPrompts&lt;CustomerServicePrompts&gt;();
///     
/// // Or scan assembly for all prompt types
/// builder.Services.AddMcpServer()
///     .WithPromptsFromAssembly();
/// </code>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpServerPromptAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerPromptAttribute"/> class.
    /// </summary>
    public McpServerPromptAttribute()
    {
    }

    /// <summary>Gets the name of the prompt.</summary>
    /// <remarks>If <see langword="null"/>, the method name will be used.</remarks>
    public string? Name { get; set; }
}
