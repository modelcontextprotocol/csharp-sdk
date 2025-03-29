namespace ModelContextProtocol.Server;

/// <summary>
/// Used to attribute a type containing methods that may be exposed as MCP tools, prompts, and resources.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerTypeAttribute : Attribute;
