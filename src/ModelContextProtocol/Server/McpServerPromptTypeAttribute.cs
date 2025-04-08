using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.Server;

/// <summary>
/// Used to attribute a type containing methods that should be exposed as MCP prompts.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is used to mark a class containing methods that should be automatically
/// discovered and registered as MCP prompts. When combined with discovery methods like
/// <see cref="McpServerBuilderExtensions.WithPromptsFromAssembly"/>, it enables automatic
/// registration of prompts without explicitly listing each prompt class.
/// </para>
/// <para>
/// Within a class marked with this attribute, individual methods that should be exposed as
/// prompts must be marked with the <see cref="McpServerPromptAttribute"/>.
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
///     [McpServerPrompt(Name = "helpPrompt")]
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
/// // Scan assembly for all prompt types
/// builder.Services.AddMcpServer()
///     .WithPromptsFromAssembly();
/// </code>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerPromptTypeAttribute : Attribute;
