using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.Server;

/// <summary>
/// Used to indicate that a method should be considered an <see cref="McpServerPrompt"/>.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is applied to methods that should be exposed as prompts in the Model Context Protocol. When a class 
/// containing methods marked with this attribute is registered with <see cref="McpServerBuilderExtensions"/>,
/// these methods become available as prompts that can be called by MCP clients.
/// </para>
/// <para>
/// When methods are provided directly to <see cref="M:McpServerPrompt.Create"/>, the attribute is not required.
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
