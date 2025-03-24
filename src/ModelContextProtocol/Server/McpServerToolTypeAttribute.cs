namespace ModelContextProtocol.Server;

/// <summary>
/// Used to attribute a type containing methods that should be exposed as MCP tools.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerToolTypeAttribute : Attribute;
