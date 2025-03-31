using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.Server;

/// <summary>
/// Used to attribute a type containing methods that should be exposed as MCP prompts.
/// </summary>
/// <remarks>
/// This is primarily relevant to methods that scan types in an assembly looking for methods
/// to expose, such as <see cref="McpServerBuilderExtensions.WithPromptsFromAssembly"/>. It is not
/// necessary to attribute types explicitly provided to a method like <see cref="McpServerBuilderExtensions.WithPrompts{TPrompt}"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerPromptTypeAttribute : Attribute;
