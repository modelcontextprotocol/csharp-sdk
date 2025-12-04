namespace ModelContextProtocol.Server;

/// <summary>
/// Specifies that a type contains methods that should be exposed as <see cref="McpServerPrompt"/>s.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is used to mark a class containing methods that should be automatically
/// discovered and registered as <see cref="McpServerPrompt"/> prompts. When combined with discovery methods like
/// WithPromptsFromAssembly, it enables automatic registration of prompts without explicitly listing each prompt class.
/// The attribute is not necessary when a reference to the type is provided directly to a method like WithPrompts.
/// </para>
/// <para>
/// Within a class marked with this attribute, individual methods that should be exposed as
/// prompts must be marked with the <see cref="McpServerPromptAttribute"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerPromptTypeAttribute : Attribute;
