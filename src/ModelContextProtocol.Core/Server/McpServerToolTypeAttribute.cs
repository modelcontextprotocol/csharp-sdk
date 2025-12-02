namespace ModelContextProtocol.Server;

/// <summary>
/// Indicates that a type contains methods that should be exposed as <see cref="McpServerTool"/>s.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is used to mark a class containing methods that should be automatically
/// discovered and registered as <see cref="McpServerTool"/>s. When combined with discovery methods like
/// WithToolsFromAssembly, it enables automatic registration of tools without explicitly listing each tool
/// class. The attribute is not necessary when a reference to the type is provided directly to a method like WithTools.
/// </para>
/// <para>
/// Within a class marked with this attribute, individual methods that should be exposed as
/// tools must be marked with the <see cref="McpServerToolAttribute"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerToolTypeAttribute : Attribute;
