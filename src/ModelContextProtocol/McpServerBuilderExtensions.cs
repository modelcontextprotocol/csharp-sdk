using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Server.Authorization;
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
        $"The non-generic {nameof(WithTools)} and {nameof(WithToolsFromAssembly)} methods require dynamic lookup of method metadata" +
        $"and may not work in Native AOT. Use the generic {nameof(WithTools)} method instead.";

    /// <summary>Adds <see cref="McpServerTool"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <typeparam name="TToolType">The tool type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="serializerOptions">The serializer options governing tool parameter marshalling.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <typeparamref name="TToolType"/>
    /// type, where the methods are attributed as <see cref="McpServerToolAttribute"/>, and adds an <see cref="McpServerTool"/>
    /// instance for each. For instance methods, an instance will be constructed for each invocation of the tool.
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
    /// <param name="builder">The builder instance.</param>
    /// <param name="tools">The <see cref="McpServerTool"/> instances to add to the server.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tools"/> is <see langword="null"/>.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="toolTypes"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <paramref name="toolTypes"/>
    /// types, where the methods are attributed as <see cref="McpServerToolAttribute"/>, and adds an <see cref="McpServerTool"/>
    /// instance for each. For instance methods, an instance will be constructed for each invocation of the tool.
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
                            services => McpServerTool.Create(toolMethod, options: new() { Services = services , SerializerOptions = serializerOptions }) :
                            services => McpServerTool.Create(toolMethod, r => CreateTarget(r.Services, toolType), new() { Services = services , SerializerOptions = serializerOptions })));
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
    /// <param name="toolAssembly">The assembly to load the types from. If <see langword="null"/>, the calling assembly will be used.</param>
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
    /// of the containing class will be constructed for each invocation of the tool.
    /// </para>
    /// <para>
    /// Tools registered through this method can be discovered by clients using the <c>list_tools</c> request
    /// and invoked using the <c>call_tool</c> request.
    /// </para>
    /// <para>
    /// Note that this method performs reflection at runtime and may not work in Native AOT scenarios. For
    /// Native AOT compatibility, consider using the generic <see cref="WithTools{TToolType}"/> method instead.
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
        $"The non-generic {nameof(WithPrompts)} and {nameof(WithPromptsFromAssembly)} methods require dynamic lookup of method metadata" +
        $"and may not work in Native AOT. Use the generic {nameof(WithPrompts)} method instead.";

    /// <summary>Adds <see cref="McpServerPrompt"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <typeparam name="TPromptType">The prompt type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="serializerOptions">The serializer options governing prompt parameter marshalling.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <typeparamref name="TPromptType"/>
    /// type, where the methods are attributed as <see cref="McpServerPromptAttribute"/>, and adds an <see cref="McpServerPrompt"/>
    /// instance for each. For instance methods, an instance will be constructed for each invocation of the prompt.
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
    /// <param name="builder">The builder instance.</param>
    /// <param name="prompts">The <see cref="McpServerPrompt"/> instances to add to the server.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="prompts"/> is <see langword="null"/>.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="promptTypes"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <paramref name="promptTypes"/>
    /// types, where the methods are attributed as <see cref="McpServerPromptAttribute"/>, and adds an <see cref="McpServerPrompt"/>
    /// instance for each. For instance methods, an instance will be constructed for each invocation of the prompt.
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
    /// <param name="promptAssembly">The assembly to load the types from. If <see langword="null"/>, the calling assembly will be used.</param>
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
    /// of the containing class will be constructed for each invocation of the prompt.
    /// </para>
    /// <para>
    /// Prompts registered through this method can be discovered by clients using the <c>list_prompts</c> request
    /// and invoked using the <c>call_prompt</c> request.
    /// </para>
    /// <para>
    /// Note that this method performs reflection at runtime and may not work in Native AOT scenarios. For
    /// Native AOT compatibility, consider using the generic <see cref="WithPrompts{TPromptType}"/> method instead.
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
        $"The non-generic {nameof(WithResources)} and {nameof(WithResourcesFromAssembly)} methods require dynamic lookup of member metadata" +
        $"and may not work in Native AOT. Use the generic {nameof(WithResources)} method instead.";

    /// <summary>Adds <see cref="McpServerResource"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <typeparam name="TResourceType">The resource type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <typeparamref name="TResourceType"/>
    /// type, where the members are attributed as <see cref="McpServerResourceAttribute"/>, and adds an <see cref="McpServerResource"/>
    /// instance for each. For instance members, an instance will be constructed for each invocation of the resource.
    /// </remarks>
    public static IMcpServerBuilder WithResources<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods |
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
    /// <param name="builder">The builder instance.</param>
    /// <param name="resourceTemplates">The <see cref="McpServerResource"/> instances to add to the server.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="resourceTemplates"/> is <see langword="null"/>.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="resourceTemplateTypes"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <paramref name="resourceTemplateTypes"/>
    /// types, where the methods are attributed as <see cref="McpServerResourceAttribute"/>, and adds an <see cref="McpServerResource"/>
    /// instance for each. For instance methods, an instance will be constructed for each invocation of the resource.
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
    /// <param name="resourceAssembly">The assembly to load the types from. If <see langword="null"/>, the calling assembly will be used.</param>
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
    /// of the containing class will be constructed for each invocation of the resource.
    /// </para>
    /// <para>
    /// Resource templates registered through this method can be discovered by clients using the <c>list_resourceTemplates</c> request
    /// and invoked using the <c>read_resource</c> request.
    /// </para>
    /// <para>
    /// Note that this method performs reflection at runtime and may not work in Native AOT scenarios. For
    /// Native AOT compatibility, consider using the generic <see cref="WithResources{TResourceType}"/> method instead.
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
    public static IMcpServerBuilder WithListResourceTemplatesHandler(this IMcpServerBuilder builder, Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, ValueTask<ListResourceTemplatesResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListResourceTemplatesHandler = handler);
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
    /// The handler can optionally support pagination via the cursor mechanism for large or dynamically-generated
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
    public static IMcpServerBuilder WithListToolsHandler(this IMcpServerBuilder builder, Func<RequestContext<ListToolsRequestParams>, CancellationToken, ValueTask<ListToolsResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListToolsHandler = handler);
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
    public static IMcpServerBuilder WithCallToolHandler(this IMcpServerBuilder builder, Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.CallToolHandler = handler);
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
    public static IMcpServerBuilder WithListPromptsHandler(this IMcpServerBuilder builder, Func<RequestContext<ListPromptsRequestParams>, CancellationToken, ValueTask<ListPromptsResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListPromptsHandler = handler);
        return builder;
    }

    /// <summary>
    /// Configures a handler for getting a prompt available from the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes prompt requests.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithGetPromptHandler(this IMcpServerBuilder builder, Func<RequestContext<GetPromptRequestParams>, CancellationToken, ValueTask<GetPromptResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.GetPromptHandler = handler);
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
    public static IMcpServerBuilder WithListResourcesHandler(this IMcpServerBuilder builder, Func<RequestContext<ListResourcesRequestParams>, CancellationToken, ValueTask<ListResourcesResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListResourcesHandler = handler);
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
    public static IMcpServerBuilder WithReadResourceHandler(this IMcpServerBuilder builder, Func<RequestContext<ReadResourceRequestParams>, CancellationToken, ValueTask<ReadResourceResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ReadResourceHandler = handler);
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
    public static IMcpServerBuilder WithCompleteHandler(this IMcpServerBuilder builder, Func<RequestContext<CompleteRequestParams>, CancellationToken, ValueTask<CompleteResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.CompleteHandler = handler);
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
    public static IMcpServerBuilder WithSubscribeToResourcesHandler(this IMcpServerBuilder builder, Func<RequestContext<SubscribeRequestParams>, CancellationToken, ValueTask<EmptyResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.SubscribeToResourcesHandler = handler);
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
    public static IMcpServerBuilder WithUnsubscribeFromResourcesHandler(this IMcpServerBuilder builder, Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, ValueTask<EmptyResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.UnsubscribeFromResourcesHandler = handler);
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
    /// and may begin sending log messages at or above the specified level to the client.
    /// </para>
    /// <para>
    /// Regardless of whether a handler is provided, an <see cref="IMcpServer"/> should itself handle
    /// such notifications by updating its <see cref="IMcpServer.LoggingLevel"/> property to return the
    /// most recently set level.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithSetLoggingLevelHandler(this IMcpServerBuilder builder, Func<RequestContext<SetLevelRequestParams>, CancellationToken, ValueTask<EmptyResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.SetLoggingLevelHandler = handler);
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
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="inputStream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="outputStream"/> is <see langword="null"/>.</exception>
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
            return McpServerFactory.Create(serverTransport, options.Value, loggerFactory, services);
        });
    }
    #endregion

    #region Tool Authorization
    /// <summary>
    /// Adds tool authorization services to the MCP server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method registers the default <see cref="IToolAuthorizationService"/> implementation
    /// that can coordinate multiple tool filters for access control.
    /// </remarks>
    public static IMcpServerBuilder WithToolAuthorization(this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);

        builder.Services.TryAddSingleton<IToolAuthorizationService, ToolAuthorizationService>();
        return builder;
    }

    /// <summary>
    /// Adds a specific tool filter to the MCP server.
    /// </summary>
    /// <typeparam name="TFilter">The type of tool filter to add.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method registers a tool filter that will be automatically discovered
    /// and used by the tool authorization system. The filter must implement <see cref="IToolFilter"/>.
    /// </remarks>
    public static IMcpServerBuilder WithToolFilter<TFilter>(this IMcpServerBuilder builder)
        where TFilter : class, IToolFilter
    {
        Throw.IfNull(builder);

        builder.Services.AddTransient<IToolFilter, TFilter>();
        return builder;
    }

    /// <summary>
    /// Adds a specific tool filter instance to the MCP server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="filter">The tool filter instance to add.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method registers a specific tool filter instance that will be used
    /// by the tool authorization system.
    /// </remarks>
    public static IMcpServerBuilder WithToolFilter(this IMcpServerBuilder builder, IToolFilter filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.AddSingleton(filter);
        return builder;
    }

    /// <summary>
    /// Adds multiple tool filters to the MCP server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="filters">The tool filter instances to add.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="filters"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method registers multiple tool filter instances that will be used
    /// by the tool authorization system.
    /// </remarks>
    public static IMcpServerBuilder WithToolFilters(this IMcpServerBuilder builder, IEnumerable<IToolFilter> filters)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filters);

        foreach (var filter in filters)
        {
            if (filter is not null)
            {
                builder.Services.AddSingleton(filter);
            }
        }

        return builder;
    }

    /// <summary>
    /// Adds tool filter aggregation to the MCP server, which automatically discovers
    /// and applies all registered tool filters.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method enables automatic discovery and coordination of all tool filters
    /// registered in the dependency injection container. This is useful when you
    /// have multiple filters and want them to be automatically applied without
    /// manual coordination.
    /// </remarks>
    public static IMcpServerBuilder WithToolFilterAggregation(this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);

        builder.Services.AddSingleton<IToolFilter, ToolFilterAggregator>();
        return builder;
    }

    /// <summary>
    /// Configures role-based tool filtering for the MCP server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="configureRoles">A delegate to configure role-based tool access.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configureRoles"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method provides a convenient way to set up role-based access control
    /// for tools using a simple configuration approach.
    /// </remarks>
    public static IMcpServerBuilder WithRoleBasedToolFiltering(this IMcpServerBuilder builder, Action<RoleBasedToolFilterBuilder> configureRoles)
    {
        Throw.IfNull(builder);
        Throw.IfNull(configureRoles);

        var roleBuilder = new RoleBasedToolFilterBuilder();
        configureRoles(roleBuilder);

        var filter = roleBuilder.Build();
        builder.Services.AddSingleton<IToolFilter>(filter);

        return builder;
    }

    /// <summary>
    /// Adds an allow-all tool filter that grants access to all tools without restrictions.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="priority">The priority for this filter. Default is <see cref="int.MaxValue"/> (lowest priority).</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This filter is useful for development environments or scenarios where
    /// no access control is required.
    /// </remarks>
    public static IMcpServerBuilder WithAllowAllToolFilter(this IMcpServerBuilder builder, int priority = int.MaxValue)
    {
        Throw.IfNull(builder);

        builder.Services.AddSingleton<IToolFilter>(new AllowAllToolFilter(priority));
        return builder;
    }

    /// <summary>
    /// Adds a deny-all tool filter that blocks access to all tools.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="priority">The priority for this filter. Default is 0 (highest priority).</param>
    /// <param name="reason">The reason for denying access. Default is "All tools denied".</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This filter is useful for lockdown scenarios or as a safety mechanism
    /// to prevent any tool execution.
    /// </remarks>
    public static IMcpServerBuilder WithDenyAllToolFilter(this IMcpServerBuilder builder, int priority = 0, string reason = "All tools denied")
    {
        Throw.IfNull(builder);

        builder.Services.AddSingleton<IToolFilter>(new DenyAllToolFilter(priority, reason));
        return builder;
    }

    /// <summary>
    /// Adds a pattern-based tool filter that allows access only to tools matching specified patterns.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="allowPatterns">Regular expression patterns that allow tool access.</param>
    /// <param name="priority">The priority for this filter. Default is 100.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="allowPatterns"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This creates an allow-list filter where only tools matching the specified
    /// regular expression patterns are allowed access.
    /// </remarks>
    public static IMcpServerBuilder WithToolAllowListFilter(this IMcpServerBuilder builder, IEnumerable<string> allowPatterns, int priority = 100)
    {
        Throw.IfNull(builder);
        Throw.IfNull(allowPatterns);

        var filter = ToolNamePatternFilter.CreateAllowList(allowPatterns, priority);
        builder.Services.AddSingleton<IToolFilter>(filter);
        return builder;
    }

    /// <summary>
    /// Adds a pattern-based tool filter that denies access to tools matching specified patterns.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="denyPatterns">Regular expression patterns that deny tool access.</param>
    /// <param name="priority">The priority for this filter. Default is 100.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="denyPatterns"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This creates a deny-list filter where tools matching the specified
    /// regular expression patterns are denied access, but all others are allowed.
    /// </remarks>
    public static IMcpServerBuilder WithToolDenyListFilter(this IMcpServerBuilder builder, IEnumerable<string> denyPatterns, int priority = 100)
    {
        Throw.IfNull(builder);
        Throw.IfNull(denyPatterns);

        var filter = ToolNamePatternFilter.CreateDenyList(denyPatterns, priority);
        builder.Services.AddSingleton<IToolFilter>(filter);
        return builder;
    }

    /// <summary>
    /// Adds a pattern-based tool filter with custom configuration.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="configureFilter">A delegate to configure the pattern filter.</param>
    /// <param name="priority">The priority for this filter. Default is 100.</param>
    /// <param name="defaultAllow">Whether to allow access by default when no patterns match. Default is false.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configureFilter"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method provides maximum flexibility for configuring pattern-based
    /// tool filtering with custom allow and deny patterns.
    /// </remarks>
    public static IMcpServerBuilder WithToolPatternFilter(this IMcpServerBuilder builder, Action<ToolNamePatternFilter> configureFilter, int priority = 100, bool defaultAllow = false)
    {
        Throw.IfNull(builder);
        Throw.IfNull(configureFilter);

        var filter = new ToolNamePatternFilter(priority, defaultAllow);
        configureFilter(filter);
        builder.Services.AddSingleton<IToolFilter>(filter);
        return builder;
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
