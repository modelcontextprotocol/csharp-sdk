using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides methods for configuring MCP servers via dependency injection.
/// </summary>
public static partial class McpServerBuilderExtensions
{
    #region WithTools
    private const string WithToolsRequiresUnreferencedCodeMessage =
        $"The non-generic {nameof(WithTools)} and {nameof(WithToolsFromAssembly)} methods require dynamic lookup of method metadata " +
        $"and might not work in Native AOT. Use the generic {nameof(WithTools)} method instead.";

    /// <summary>Adds <see cref="McpServerTool"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <typeparam name="TToolType">The tool type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="serializerOptions">The serializer options governing tool parameter marshalling.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <typeparamref name="TToolType"/>
    /// type, where the methods are attributed as <see cref="McpServerToolAttribute"/>, and adds an <see cref="McpServerTool"/>
    /// instance for each. For instance methods, an instance is constructed for each invocation of the tool.
    /// </remarks>
    public static IMcpServerBuilder WithTools<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.PublicConstructors)] TToolType>(
        this IMcpServerBuilder builder,
        JsonSerializerOptions? serializerOptions = null)
    {
        Throw.IfNull(builder);

        foreach (var toolMethod in typeof(TToolType).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (toolMethod.GetCustomAttribute<McpServerToolAttribute>() is not null)
            {
                builder.Services.AddSingleton((Func<IServiceProvider, McpServerTool>)(toolMethod.IsStatic ?
                    services => McpServerTool.Create(toolMethod, options: new() { Services = services, SerializerOptions = serializerOptions }) :
                    services => McpServerTool.Create(toolMethod, static r => CreateTarget(r.Services, typeof(TToolType)), new() { Services = services, SerializerOptions = serializerOptions })));
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerTool"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <typeparam name="TToolType">The tool type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="target">The target instance from which the tools should be sourced.</param>
    /// <param name="serializerOptions">The serializer options governing tool parameter marshalling.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="target"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method discovers all methods (public and non-public) on the specified <typeparamref name="TToolType"/>
    /// type, where the methods are attributed as <see cref="McpServerToolAttribute"/>, and adds an <see cref="McpServerTool"/>
    /// instance for each, using <paramref name="target"/> as the associated instance for instance methods.
    /// </para>
    /// <para>
    /// However, if <typeparamref name="TToolType"/> is itself an <see cref="IEnumerable{T}"/> of <see cref="McpServerTool"/>,
    /// this method registers those tools directly without scanning for methods on <typeparamref name="TToolType"/>.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithTools<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods)] TToolType>(
        this IMcpServerBuilder builder,
        TToolType target,
        JsonSerializerOptions? serializerOptions = null)
    {
        Throw.IfNull(builder);
        Throw.IfNull(target);

        if (target is IEnumerable<McpServerTool> tools)
        {
            return builder.WithTools(tools);
        }

        foreach (var toolMethod in typeof(TToolType).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (toolMethod.GetCustomAttribute<McpServerToolAttribute>() is not null)
            {
                builder.Services.AddSingleton(services => McpServerTool.Create(
                    toolMethod,
                    toolMethod.IsStatic ? null : target,
                    new() { Services = services, SerializerOptions = serializerOptions }));
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerTool"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="tools">The <see cref="McpServerTool"/> instances to add to the server.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="tools"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithTools(this IMcpServerBuilder builder, IEnumerable<McpServerTool> tools)
    {
        Throw.IfNull(builder);
        Throw.IfNull(tools);

        foreach (var tool in tools)
        {
            if (tool is not null)
            {
                builder.Services.AddSingleton(tool);
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerTool"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="toolTypes">Types with <see cref="McpServerToolAttribute"/>-attributed methods to add as tools to the server.</param>
    /// <param name="serializerOptions">The serializer options governing tool parameter marshalling.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="toolTypes"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <paramref name="toolTypes"/>
    /// types, where the methods are attributed as <see cref="McpServerToolAttribute"/>, and adds an <see cref="McpServerTool"/>
    /// instance for each. For instance methods, an instance is constructed for each invocation of the tool.
    /// </remarks>
    [RequiresUnreferencedCode(WithToolsRequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithTools(this IMcpServerBuilder builder, IEnumerable<Type> toolTypes, JsonSerializerOptions? serializerOptions = null)
    {
        Throw.IfNull(builder);
        Throw.IfNull(toolTypes);

        foreach (var toolType in toolTypes)
        {
            if (toolType is not null)
            {
                foreach (var toolMethod in toolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (toolMethod.GetCustomAttribute<McpServerToolAttribute>() is not null)
                    {
                        builder.Services.AddSingleton((Func<IServiceProvider, McpServerTool>)(toolMethod.IsStatic ?
                            services => McpServerTool.Create(toolMethod, options: new() { Services = services, SerializerOptions = serializerOptions }) :
                            services => McpServerTool.Create(toolMethod, r => CreateTarget(r.Services, toolType), new() { Services = services, SerializerOptions = serializerOptions })));
                    }
                }
            }
        }

        return builder;
    }

    /// <summary>
    /// Adds types marked with the <see cref="McpServerToolTypeAttribute"/> attribute from the given assembly as tools to the server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="serializerOptions">The serializer options governing tool parameter marshalling.</param>
    /// <param name="toolAssembly">The assembly to load the types from. If <see langword="null"/>, the calling assembly is used.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method scans the specified assembly (or the calling assembly if none is provided) for classes
    /// marked with the <see cref="McpServerToolTypeAttribute"/>. It then discovers all methods within those
    /// classes that are marked with the <see cref="McpServerToolAttribute"/> and registers them as <see cref="McpServerTool"/>s
    /// in the <paramref name="builder"/>'s <see cref="IServiceCollection"/>.
    /// </para>
    /// <para>
    /// The method automatically handles both static and instance methods. For instance methods, a new instance
    /// of the containing class is constructed for each invocation of the tool.
    /// </para>
    /// <para>
    /// Tools registered through this method can be discovered by clients using the <c>list_tools</c> request
    /// and invoked using the <c>call_tool</c> request.
    /// </para>
    /// <para>
    /// Note that this method performs reflection at runtime and might not work in Native AOT scenarios. For
    /// Native AOT compatibility, consider using the generic <see cref="M:WithTools"/> method instead.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(WithToolsRequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithToolsFromAssembly(this IMcpServerBuilder builder, Assembly? toolAssembly = null, JsonSerializerOptions? serializerOptions = null)
    {
        Throw.IfNull(builder);

        toolAssembly ??= Assembly.GetCallingAssembly();

        return builder.WithTools(
            from t in toolAssembly.GetTypes()
            where t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
            select t,
            serializerOptions);
    }
    #endregion

    #region WithPrompts
    private const string WithPromptsRequiresUnreferencedCodeMessage =
        $"The non-generic {nameof(WithPrompts)} and {nameof(WithPromptsFromAssembly)} methods require dynamic lookup of method metadata " +
        $"and might not work in Native AOT. Use the generic {nameof(WithPrompts)} method instead.";

    /// <summary>Adds <see cref="McpServerPrompt"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <typeparam name="TPromptType">The prompt type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="serializerOptions">The serializer options governing prompt parameter marshalling.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <typeparamref name="TPromptType"/>
    /// type, where the methods are attributed as <see cref="McpServerPromptAttribute"/>, and adds an <see cref="McpServerPrompt"/>
    /// instance for each. For instance methods, an instance is constructed for each invocation of the prompt.
    /// </remarks>
    public static IMcpServerBuilder WithPrompts<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.PublicConstructors)] TPromptType>(
        this IMcpServerBuilder builder,
        JsonSerializerOptions? serializerOptions = null)
    {
        Throw.IfNull(builder);

        foreach (var promptMethod in typeof(TPromptType).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (promptMethod.GetCustomAttribute<McpServerPromptAttribute>() is not null)
            {
                builder.Services.AddSingleton((Func<IServiceProvider, McpServerPrompt>)(promptMethod.IsStatic ?
                    services => McpServerPrompt.Create(promptMethod, options: new() { Services = services, SerializerOptions = serializerOptions }) :
                    services => McpServerPrompt.Create(promptMethod, static r => CreateTarget(r.Services, typeof(TPromptType)), new() { Services = services, SerializerOptions = serializerOptions })));
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerPrompt"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <typeparam name="TPromptType">The prompt type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="target">The target instance from which the prompts should be sourced.</param>
    /// <param name="serializerOptions">The serializer options governing prompt parameter marshalling.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="target"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method discovers all methods (public and non-public) on the specified <typeparamref name="TPromptType"/>
    /// type, where the methods are attributed as <see cref="McpServerPromptAttribute"/>, and adds an <see cref="McpServerPrompt"/>
    /// instance for each, using <paramref name="target"/> as the associated instance for instance methods.
    /// </para>
    /// <para>
    /// However, if <typeparamref name="TPromptType"/> is itself an <see cref="IEnumerable{T}"/> of <see cref="McpServerPrompt"/>,
    /// this method registers those prompts directly without scanning for methods on <typeparamref name="TPromptType"/>.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithPrompts<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods)] TPromptType>(
        this IMcpServerBuilder builder,
        TPromptType target,
        JsonSerializerOptions? serializerOptions = null)
    {
        Throw.IfNull(builder);
        Throw.IfNull(target);

        if (target is IEnumerable<McpServerPrompt> prompts)
        {
            return builder.WithPrompts(prompts);
        }

        foreach (var promptMethod in typeof(TPromptType).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (promptMethod.GetCustomAttribute<McpServerPromptAttribute>() is not null)
            {
                builder.Services.AddSingleton(services => McpServerPrompt.Create(promptMethod, target, new() { Services = services, SerializerOptions = serializerOptions }));
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerPrompt"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="prompts">The <see cref="McpServerPrompt"/> instances to add to the server.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="prompts"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithPrompts(this IMcpServerBuilder builder, IEnumerable<McpServerPrompt> prompts)
    {
        Throw.IfNull(builder);
        Throw.IfNull(prompts);

        foreach (var prompt in prompts)
        {
            if (prompt is not null)
            {
                builder.Services.AddSingleton(prompt);
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerPrompt"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="promptTypes">Types with marked methods to add as prompts to the server.</param>
    /// <param name="serializerOptions">The serializer options governing prompt parameter marshalling.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="promptTypes"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <paramref name="promptTypes"/>
    /// types, where the methods are attributed as <see cref="McpServerPromptAttribute"/>, and adds an <see cref="McpServerPrompt"/>
    /// instance for each. For instance methods, an instance is constructed for each invocation of the prompt.
    /// </remarks>
    [RequiresUnreferencedCode(WithPromptsRequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithPrompts(this IMcpServerBuilder builder, IEnumerable<Type> promptTypes, JsonSerializerOptions? serializerOptions = null)
    {
        Throw.IfNull(builder);
        Throw.IfNull(promptTypes);

        foreach (var promptType in promptTypes)
        {
            if (promptType is not null)
            {
                foreach (var promptMethod in promptType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (promptMethod.GetCustomAttribute<McpServerPromptAttribute>() is not null)
                    {
                        builder.Services.AddSingleton((Func<IServiceProvider, McpServerPrompt>)(promptMethod.IsStatic ?
                            services => McpServerPrompt.Create(promptMethod, options: new() { Services = services, SerializerOptions = serializerOptions }) :
                            services => McpServerPrompt.Create(promptMethod, r => CreateTarget(r.Services, promptType), new() { Services = services, SerializerOptions = serializerOptions })));
                    }
                }
            }
        }

        return builder;
    }

    /// <summary>
    /// Adds types marked with the <see cref="McpServerPromptTypeAttribute"/> attribute from the given assembly as prompts to the server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="serializerOptions">The serializer options governing prompt parameter marshalling.</param>
    /// <param name="promptAssembly">The assembly to load the types from. If <see langword="null"/>, the calling assembly is used.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method scans the specified assembly (or the calling assembly if none is provided) for classes
    /// marked with the <see cref="McpServerPromptTypeAttribute"/>. It then discovers all methods within those
    /// classes that are marked with the <see cref="McpServerPromptAttribute"/> and registers them as <see cref="McpServerPrompt"/>s
    /// in the <paramref name="builder"/>'s <see cref="IServiceCollection"/>.
    /// </para>
    /// <para>
    /// The method automatically handles both static and instance methods. For instance methods, a new instance
    /// of the containing class is constructed for each invocation of the prompt.
    /// </para>
    /// <para>
    /// Prompts registered through this method can be discovered by clients using the <c>list_prompts</c> request
    /// and invoked using the <c>prompts/get</c> request.
    /// </para>
    /// <para>
    /// Note that this method performs reflection at runtime and might not work in Native AOT scenarios. For
    /// Native AOT compatibility, consider using the generic <see cref="M:WithPrompts"/> method instead.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(WithPromptsRequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithPromptsFromAssembly(this IMcpServerBuilder builder, Assembly? promptAssembly = null, JsonSerializerOptions? serializerOptions = null)
    {
        Throw.IfNull(builder);

        promptAssembly ??= Assembly.GetCallingAssembly();

        return builder.WithPrompts(
            from t in promptAssembly.GetTypes()
            where t.GetCustomAttribute<McpServerPromptTypeAttribute>() is not null
            select t,
            serializerOptions);
    }
    #endregion

    #region WithResources
    private const string WithResourcesRequiresUnreferencedCodeMessage =
        $"The non-generic {nameof(WithResources)} and {nameof(WithResourcesFromAssembly)} methods require dynamic lookup of member metadata " +
        $"and might not work in Native AOT. Use the generic {nameof(WithResources)} method instead.";

    /// <summary>Adds <see cref="McpServerResource"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <typeparam name="TResourceType">The resource type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <typeparamref name="TResourceType"/>
    /// type, where the members are attributed as <see cref="McpServerResourceAttribute"/>, and adds an <see cref="McpServerResource"/>
    /// instance for each. For instance members, an instance is constructed for each invocation of the resource.
    /// </remarks>
    public static IMcpServerBuilder WithResources<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.PublicConstructors)] TResourceType>(
        this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);

        foreach (var resourceTemplateMethod in typeof(TResourceType).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (resourceTemplateMethod.GetCustomAttribute<McpServerResourceAttribute>() is not null)
            {
                builder.Services.AddSingleton((Func<IServiceProvider, McpServerResource>)(resourceTemplateMethod.IsStatic ?
                    services => McpServerResource.Create(resourceTemplateMethod, options: new() { Services = services }) :
                    services => McpServerResource.Create(resourceTemplateMethod, static r => CreateTarget(r.Services, typeof(TResourceType)), new() { Services = services })));
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerResource"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <typeparam name="TResourceType">The resource type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="target">The target instance from which the resources should be sourced.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="target"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method discovers all methods (public and non-public) on the specified <typeparamref name="TResourceType"/>
    /// type, where the methods are attributed as <see cref="McpServerResourceAttribute"/>, and adds an <see cref="McpServerResource"/>
    /// instance for each, using <paramref name="target"/> as the associated instance for instance methods.
    /// </para>
    /// <para>
    /// However, if <typeparamref name="TResourceType"/> is itself an <see cref="IEnumerable{T}"/> of <see cref="McpServerResource"/>,
    /// this method registers those resources directly without scanning for methods on <typeparamref name="TResourceType"/>.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithResources<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods)] TResourceType>(
        this IMcpServerBuilder builder,
        TResourceType target)
    {
        Throw.IfNull(builder);
        Throw.IfNull(target);

        if (target is IEnumerable<McpServerResource> resources)
        {
            return builder.WithResources(resources);
        }

        foreach (var resourceTemplateMethod in typeof(TResourceType).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (resourceTemplateMethod.GetCustomAttribute<McpServerResourceAttribute>() is not null)
            {
                builder.Services.AddSingleton(services => McpServerResource.Create(resourceTemplateMethod, target, new() { Services = services }));
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerResource"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="resourceTemplates">The <see cref="McpServerResource"/> instances to add to the server.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="resourceTemplates"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithResources(this IMcpServerBuilder builder, IEnumerable<McpServerResource> resourceTemplates)
    {
        Throw.IfNull(builder);
        Throw.IfNull(resourceTemplates);

        foreach (var resourceTemplate in resourceTemplates)
        {
            if (resourceTemplate is not null)
            {
                builder.Services.AddSingleton(resourceTemplate);
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerResource"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="resourceTemplateTypes">Types with marked methods to add as resources to the server.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="resourceTemplateTypes"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <paramref name="resourceTemplateTypes"/>
    /// types, where the methods are attributed as <see cref="McpServerResourceAttribute"/>, and adds an <see cref="McpServerResource"/>
    /// instance for each. For instance methods, an instance is constructed for each invocation of the resource.
    /// </remarks>
    [RequiresUnreferencedCode(WithResourcesRequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithResources(this IMcpServerBuilder builder, IEnumerable<Type> resourceTemplateTypes)
    {
        Throw.IfNull(builder);
        Throw.IfNull(resourceTemplateTypes);

        foreach (var resourceTemplateType in resourceTemplateTypes)
        {
            if (resourceTemplateType is not null)
            {
                foreach (var resourceTemplateMethod in resourceTemplateType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (resourceTemplateMethod.GetCustomAttribute<McpServerResourceAttribute>() is not null)
                    {
                        builder.Services.AddSingleton((Func<IServiceProvider, McpServerResource>)(resourceTemplateMethod.IsStatic ?
                            services => McpServerResource.Create(resourceTemplateMethod, options: new() { Services = services }) :
                            services => McpServerResource.Create(resourceTemplateMethod, r => CreateTarget(r.Services, resourceTemplateType), new() { Services = services })));
                    }
                }
            }
        }

        return builder;
    }

    /// <summary>
    /// Adds types marked with the <see cref="McpServerResourceTypeAttribute"/> attribute from the given assembly as resources to the server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="resourceAssembly">The assembly to load the types from. If <see langword="null"/>, the calling assembly is used.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method scans the specified assembly (or the calling assembly if none is provided) for classes
    /// marked with the <see cref="McpServerResourceTypeAttribute"/>. It then discovers all members within those
    /// classes that are marked with the <see cref="McpServerResourceAttribute"/> and registers them as <see cref="McpServerResource"/>s
    /// in the <paramref name="builder"/>'s <see cref="IServiceCollection"/>.
    /// </para>
    /// <para>
    /// The method automatically handles both static and instance members. For instance members, a new instance
    /// of the containing class is constructed for each invocation of the resource.
    /// </para>
    /// <para>
    /// Resource templates registered through this method can be discovered by clients using the <c>list_resourceTemplates</c> request
    /// and invoked using the <c>read_resource</c> request.
    /// </para>
    /// <para>
    /// Note that this method performs reflection at runtime and might not work in Native AOT scenarios. For
    /// Native AOT compatibility, consider using the generic <see cref="M:WithResources"/> method instead.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(WithResourcesRequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithResourcesFromAssembly(this IMcpServerBuilder builder, Assembly? resourceAssembly = null)
    {
        Throw.IfNull(builder);

        resourceAssembly ??= Assembly.GetCallingAssembly();

        return builder.WithResources(
            from t in resourceAssembly.GetTypes()
            where t.GetCustomAttribute<McpServerResourceTypeAttribute>() is not null
            select t);
    }
    #endregion

    #region Handlers
    /// <summary>
    /// Configures a handler for listing resource templates available from the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes resource template list requests.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This handler is responsible for providing clients with information about available resource templates
    /// that can be used to construct resource URIs.
    /// </para>
    /// <para>
    /// Resource templates describe the structure of resource URIs that the server can handle. They include
    /// URI templates (according to RFC 6570) that clients can use to construct valid resource URIs.
    /// </para>
    /// <para>
    /// This handler is typically paired with <see cref="WithReadResourceHandler"/> to provide a complete
    /// resource system where templates define the URI patterns and the read handler provides the actual content.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithListResourceTemplatesHandler(this IMcpServerBuilder builder, McpRequestHandler<ListResourceTemplatesRequestParams, ListResourceTemplatesResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.ListResourceTemplatesHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Resources ??= new();
        });
        return builder;
    }

    /// <summary>
    /// Configures a handler for listing tools available from the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler that processes list tools requests.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This handler is called when a client requests a list of available tools. It should return all tools
    /// that can be invoked through the server, including their names, descriptions, and parameter specifications.
    /// The handler can optionally support pagination via the cursor mechanism for large or dynamically generated
    /// tool collections.
    /// </para>
    /// <para>
    /// When tools are also defined using <see cref="McpServerTool"/> collection, both sets of tools
    /// will be combined in the response to clients. This allows for a mix of programmatically defined
    /// tools and dynamically generated tools.
    /// </para>
    /// <para>
    /// This method is typically paired with <see cref="WithCallToolHandler"/> to provide a complete tools implementation,
    /// where <see cref="WithListToolsHandler"/> advertises available tools and <see cref="WithCallToolHandler"/>
    /// executes them when invoked by clients.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithListToolsHandler(this IMcpServerBuilder builder, McpRequestHandler<ListToolsRequestParams, ListToolsResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.ListToolsHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Tools ??= new();
        });
        return builder;
    }

    /// <summary>
    /// Configures a handler for calling tools available from the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes tool calls.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The call tool handler is responsible for executing custom tools and returning their results to clients.
    /// This method is typically paired with <see cref="WithListToolsHandler"/> to provide a complete tools implementation,
    /// where <see cref="WithListToolsHandler"/> advertises available tools and this handler executes them.
    /// </remarks>
    public static IMcpServerBuilder WithCallToolHandler(this IMcpServerBuilder builder, McpRequestHandler<CallToolRequestParams, CallToolResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.CallToolHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Tools ??= new();
        });
        return builder;
    }

    /// <summary>
    /// Configures a handler for listing prompts available from the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler that processes list prompts requests.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This handler is called when a client requests a list of available prompts. It should return all prompts
    /// that can be invoked through the server, including their names, descriptions, and parameter specifications.
    /// The handler can optionally support pagination via the cursor mechanism for large or dynamically-generated
    /// prompt collections.
    /// </para>
    /// <para>
    /// When prompts are also defined using <see cref="McpServerPrompt"/> collection, both sets of prompts
    /// will be combined in the response to clients. This allows for a mix of programmatically defined
    /// prompts and dynamically generated prompts.
    /// </para>
    /// <para>
    /// This method is typically paired with <see cref="WithGetPromptHandler"/> to provide a complete prompts implementation,
    /// where <see cref="WithListPromptsHandler"/> advertises available prompts and <see cref="WithGetPromptHandler"/>
    /// produces them when invoked by clients.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithListPromptsHandler(this IMcpServerBuilder builder, McpRequestHandler<ListPromptsRequestParams, ListPromptsResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.ListPromptsHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Prompts ??= new();
        });
        return builder;
    }

    /// <summary>
    /// Configures a handler for getting a prompt available from the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes prompt requests.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithGetPromptHandler(this IMcpServerBuilder builder, McpRequestHandler<GetPromptRequestParams, GetPromptResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.GetPromptHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Prompts ??= new();
        });
        return builder;
    }

    /// <summary>
    /// Configures a handler for listing resources available from the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes resource list requests.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This handler is typically paired with <see cref="WithReadResourceHandler"/> to provide a complete resources implementation,
    /// where this handler advertises available resources and the read handler provides their content when requested.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithListResourcesHandler(this IMcpServerBuilder builder, McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.ListResourcesHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Resources ??= new();
        });
        return builder;
    }

    /// <summary>
    /// Configures a handler for reading a resource available from the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes resource read requests.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This handler is typically paired with <see cref="WithListResourcesHandler"/> to provide a complete resources implementation,
    /// where the list handler advertises available resources and the read handler provides their content when requested.
    /// </remarks>
    public static IMcpServerBuilder WithReadResourceHandler(this IMcpServerBuilder builder, McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.ReadResourceHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Resources ??= new();
        });
        return builder;
    }

    /// <summary>
    /// Configures a handler for auto-completion suggestions for prompt arguments or resource references available from the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes completion requests.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The completion handler is invoked when clients request suggestions for argument values.
    /// This enables auto-complete functionality for both prompt arguments and resource references.
    /// </remarks>
    public static IMcpServerBuilder WithCompleteHandler(this IMcpServerBuilder builder, McpRequestHandler<CompleteRequestParams, CompleteResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.CompleteHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Completions ??= new();
        });
        return builder;
    }

    /// <summary>
    /// Configures a handler for resource subscription requests.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes resource subscription requests.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The subscribe handler is responsible for registering client interest in specific resources. When a resource
    /// changes, the server can notify all subscribed clients about the change.
    /// </para>
    /// <para>
    /// This handler is typically paired with <see cref="WithUnsubscribeFromResourcesHandler"/> to provide a complete
    /// subscription management system. Resource subscriptions allow clients to maintain up-to-date information without
    /// needing to poll resources constantly.
    /// </para>
    /// <para>
    /// After registering a subscription, it's the server's responsibility to track which client is subscribed to which
    /// resources and to send appropriate notifications through the connection when resources change.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithSubscribeToResourcesHandler(this IMcpServerBuilder builder, McpRequestHandler<SubscribeRequestParams, EmptyResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.SubscribeToResourcesHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Resources ??= new();
            options.Capabilities.Resources.Subscribe = true;
        });
        return builder;
    }

    /// <summary>
    /// Configures a handler for resource unsubscription requests.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes resource unsubscription requests.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The unsubscribe handler is responsible for removing client interest in specific resources. When a client
    /// no longer needs to receive notifications about resource changes, it can send an unsubscribe request.
    /// </para>
    /// <para>
    /// This handler is typically paired with <see cref="WithSubscribeToResourcesHandler"/> to provide a complete
    /// subscription management system. The unsubscribe operation is idempotent, meaning it can be called multiple
    /// times for the same resource without causing errors, even if there is no active subscription.
    /// </para>
    /// <para>
    /// After removing a subscription, the server should stop sending notifications to the client about changes
    /// to the specified resource.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithUnsubscribeFromResourcesHandler(this IMcpServerBuilder builder, McpRequestHandler<UnsubscribeRequestParams, EmptyResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.UnsubscribeFromResourcesHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Resources ??= new();
            options.Capabilities.Resources.Subscribe = true;
        });
        return builder;
    }

    /// <summary>
    /// Configures a handler for processing logging level change requests from clients.
    /// </summary>
    /// <param name="builder">The server builder instance.</param>
    /// <param name="handler">The handler that processes requests to change the logging level.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// When a client sends a <c>logging/setLevel</c> request, this handler will be invoked to process
    /// the requested level change. The server typically adjusts its internal logging level threshold
    /// and might begin sending log messages at or above the specified level to the client.
    /// </para>
    /// <para>
    /// Regardless of whether a handler is provided, an <see cref="McpServer"/> should itself handle
    /// such notifications by updating its <see cref="McpServer.LoggingLevel"/> property to return the
    /// most recently set level.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithSetLoggingLevelHandler(this IMcpServerBuilder builder, McpRequestHandler<SetLevelRequestParams, EmptyResult> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.SetLoggingLevelHandler = handler;
            options.Capabilities ??= new();
            options.Capabilities.Logging ??= new();
        });
        return builder;
    }
    #endregion

    #region Filters
    /// <summary>
    /// Configures message-level filters for the MCP server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="configure">A callback used to register message filters.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithMessageFilters(this IMcpServerBuilder builder, Action<IMcpMessageFilterBuilder> configure)
    {
        Throw.IfNull(builder);
        Throw.IfNull(configure);

        configure(new DefaultMcpMessageFilterBuilder(builder));
        return builder;
    }

    /// <summary>
    /// Configures request-specific filters for the MCP server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="configure">A callback used to register request-specific filters.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithRequestFilters(this IMcpServerBuilder builder, Action<IMcpRequestFilterBuilder> configure)
    {
        Throw.IfNull(builder);
        Throw.IfNull(configure);

        configure(new DefaultMcpRequestFilterBuilder(builder));
        return builder;
    }

    #endregion

    #region Transports
    /// <summary>
    /// Adds a server transport that uses standard input (stdin) and standard output (stdout) for communication.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method configures the server to communicate using the standard input and output streams,
    /// which is commonly used when the Model Context Protocol server is launched locally by a client process.
    /// </para>
    /// <para>
    /// When using this transport, the server runs as a single-session service that exits when the
    /// stdin stream is closed. This makes it suitable for scenarios where the server should terminate
    /// when the parent process disconnects.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithStdioServerTransport(this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);

        AddSingleSessionServerDependencies(builder.Services);
        builder.Services.AddSingleton<ITransport>(sp =>
        {
            var serverOptions = sp.GetRequiredService<IOptions<McpServerOptions>>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new StdioServerTransport(serverOptions.Value, loggerFactory);
        });

        return builder;
    }

    /// <summary>
    /// Adds a server transport that uses the specified input and output streams for communication.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="inputStream">The input <see cref="Stream"/> to use as standard input.</param>
    /// <param name="outputStream">The output <see cref="Stream"/> to use as standard output.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="inputStream"/> or <paramref name="outputStream"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithStreamServerTransport(
        this IMcpServerBuilder builder,
        Stream inputStream,
        Stream outputStream)
    {
        Throw.IfNull(builder);
        Throw.IfNull(inputStream);
        Throw.IfNull(outputStream);

        AddSingleSessionServerDependencies(builder.Services);
        builder.Services.AddSingleton<ITransport>(new StreamServerTransport(inputStream, outputStream));

        return builder;
    }

    private static void AddSingleSessionServerDependencies(IServiceCollection services)
    {
        services.AddHostedService<SingleSessionMcpServerHostedService>();
        services.TryAddSingleton(services =>
        {
            ITransport serverTransport = services.GetRequiredService<ITransport>();
            IOptions<McpServerOptions> options = services.GetRequiredService<IOptions<McpServerOptions>>();
            ILoggerFactory? loggerFactory = services.GetService<ILoggerFactory>();
            return McpServer.Create(serverTransport, options.Value, loggerFactory, services);
        });
    }
    #endregion

    #region Helpers
    /// <summary>Creates an instance of the target object.</summary>
    private static object CreateTarget(
        IServiceProvider? services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type) =>
        services is not null ? ActivatorUtilities.CreateInstance(services, type) :
        Activator.CreateInstance(type)!;
    #endregion
}
