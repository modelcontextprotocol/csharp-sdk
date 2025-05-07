using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.Server;

/// <summary>
/// Used to attribute a type containing methods that should be exposed as <see cref="McpServerResourceTemplate"/>s.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is used to mark a class containing methods that should be automatically
/// discovered and registered as <see cref="McpServerResourceTemplate"/>s. When combined with discovery methods like
/// <see cref="McpServerBuilderExtensions.WithResourceTemplatesFromAssembly"/>, it enables automatic registration 
/// of resouce templates without explicitly listing each resource template class. The attribute is not necessary when a reference
/// to the type is provided directly to a method like <see cref="McpServerBuilderExtensions.WithResourceTemplates{T}"/>.
/// </para>
/// <para>
/// Within a class marked with this attribute, individual methods that should be exposed as
/// resource templates must be marked with the <see cref="McpServerResourceTemplateAttribute"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerResourceTemplateTypeAttribute : Attribute;
