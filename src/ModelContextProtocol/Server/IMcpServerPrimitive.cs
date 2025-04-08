namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an MCP server primitive, like a tool or a prompt.
/// </summary>
/// <remarks>
/// <para>
/// MCP server primitives are the building blocks of MCP servers, providing functionality
/// that can be exposed to clients. The two main types of primitives are tools (which perform actions)
/// and prompts (which provide pre-defined text templates).
/// </para>
/// <para>
/// In the Model Context Protocol architecture, primitives are fundamental components that 
/// enable communication between AI models and applications. They provide a standardized way 
/// to expose functionality that models can utilize during inference, allowing for dynamic 
/// interactions between the model and external systems.
/// </para>
/// <para>
/// IMcpServerPrimitive is implemented by <see cref="McpServerTool"/> and <see cref="McpServerPrompt"/>,
/// which provide concrete implementations for tools and prompts respectively. These primitives
/// are registered with the MCP server and made available to client applications.
/// </para>
/// <para>
/// Example primitive implementations can be found in the samples, such as:
/// <code>
/// [McpServerToolType]
/// public class MyTools
/// {
///     [McpServerTool(Name = "echo")]
///     public static string Echo(IMcpServer server, string message)
///     {
///         return $"Echo: {message}";
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public interface IMcpServerPrimitive
{
    /// <summary>Gets the name of the primitive.</summary>
    string Name { get; }
}
