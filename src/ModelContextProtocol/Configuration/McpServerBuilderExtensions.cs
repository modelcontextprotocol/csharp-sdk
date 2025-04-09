using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Hosting;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

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
        this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);

        foreach (var toolMethod in typeof(TToolType).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (toolMethod.GetCustomAttribute<McpServerToolAttribute>() is not null)
            {
                builder.Services.AddSingleton((Func<IServiceProvider, McpServerTool>)(toolMethod.IsStatic ?
                    services => McpServerTool.Create(toolMethod, options: new() { Services = services }) :
                    services => McpServerTool.Create(toolMethod, typeof(TToolType), new() { Services = services })));
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerTool"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="toolTypes">Types with marked methods to add as tools to the server.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="toolTypes"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <paramref name="toolTypes"/>
    /// types, where the methods are attributed as <see cref="McpServerToolAttribute"/>, and adds an <see cref="McpServerTool"/>
    /// instance for each. For instance methods, an instance will be constructed for each invocation of the tool.
    /// </remarks>
    [RequiresUnreferencedCode(WithToolsRequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithTools(this IMcpServerBuilder builder, params IEnumerable<Type> toolTypes)
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
                            services => McpServerTool.Create(toolMethod, options: new() { Services = services }) :
                            services => McpServerTool.Create(toolMethod, toolType, new() { Services = services })));
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
    /// <param name="toolAssembly">The assembly to load the types from. Null to get the current assembly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method scans the specified assembly (or the calling assembly if none is provided) for classes
    /// marked with the <see cref="McpServerToolTypeAttribute"/>. It then discovers all methods within those
    /// classes that are marked with the <see cref="McpServerToolAttribute"/> and registers them as tools 
    /// in the Model Context Protocol server.
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
    /// <example>
    /// <para>
    /// Register tools from the current assembly:
    /// <code>
    /// // In a class library with tool classes
    /// services.AddMcpServer()
    ///     .WithToolsFromAssembly();
    /// </code>
    /// </para>
    /// <para>
    /// Register tools from a specific assembly:
    /// <code>
    /// // Load tools from a specific assembly
    /// services.AddMcpServer()
    ///     .WithToolsFromAssembly(typeof(ExternalTools).Assembly);
    /// </code>
    /// </para>
    /// </example>
    /// <seealso cref="WithTools{TToolType}"/>
    /// <seealso cref="WithTools(IMcpServerBuilder, IEnumerable{Type})"/>
    /// <seealso cref="McpServerToolTypeAttribute"/>
    /// <seealso cref="McpServerToolAttribute"/>
    [RequiresUnreferencedCode(WithToolsRequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithToolsFromAssembly(this IMcpServerBuilder builder, Assembly? toolAssembly = null)
    {
        Throw.IfNull(builder);

        toolAssembly ??= Assembly.GetCallingAssembly();

        return builder.WithTools(
            from t in toolAssembly.GetTypes()
            where t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
            select t);
    }
    #endregion

    #region WithPrompts
    private const string WithPromptsRequiresUnreferencedCodeMessage =
        $"The non-generic {nameof(WithPrompts)} and {nameof(WithPromptsFromAssembly)} methods require dynamic lookup of method metadata" +
        $"and may not work in Native AOT. Use the generic {nameof(WithPrompts)} method instead.";

    /// <summary>Adds <see cref="McpServerPrompt"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <typeparam name="TPromptType">The prompt type.</typeparam>
    /// <param name="builder">The builder instance.</param>
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
        this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);

        foreach (var promptMethod in typeof(TPromptType).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (promptMethod.GetCustomAttribute<McpServerPromptAttribute>() is not null)
            {
                builder.Services.AddSingleton((Func<IServiceProvider, McpServerPrompt>)(promptMethod.IsStatic ?
                    services => McpServerPrompt.Create(promptMethod, options: new() { Services = services }) :
                    services => McpServerPrompt.Create(promptMethod, typeof(TPromptType), new() { Services = services })));
            }
        }

        return builder;
    }

    /// <summary>Adds <see cref="McpServerPrompt"/> instances to the service collection backing <paramref name="builder"/>.</summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="promptTypes">Types with marked methods to add as prompts to the server.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="promptTypes"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method discovers all instance and static methods (public and non-public) on the specified <paramref name="promptTypes"/>
    /// types, where the methods are attributed as <see cref="McpServerPromptAttribute"/>, and adds an <see cref="McpServerPrompt"/>
    /// instance for each. For instance methods, an instance will be constructed for each invocation of the prompt.
    /// </remarks>
    [RequiresUnreferencedCode(WithPromptsRequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithPrompts(this IMcpServerBuilder builder, params IEnumerable<Type> promptTypes)
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
                            services => McpServerPrompt.Create(promptMethod, options: new() { Services = services }) :
                            services => McpServerPrompt.Create(promptMethod, promptType, new() { Services = services })));
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
    /// <param name="promptAssembly">The assembly to load the types from. Null to get the current assembly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method scans the specified assembly (or the calling assembly if none is provided) for classes
    /// marked with the <see cref="McpServerPromptTypeAttribute"/>. It then discovers all methods within those
    /// classes that are marked with the <see cref="McpServerPromptAttribute"/> and registers them as prompts 
    /// in the Model Context Protocol server.
    /// </para>
    /// <para>
    /// The method automatically handles both static and instance methods. For instance methods, a new instance
    /// of the containing class will be constructed for each invocation of the prompt.
    /// </para>
    /// <para>
    /// Prompts registered through this method can be discovered by clients using the <c>list_prompts</c> request
    /// and invoked using the <c>get_prompt</c> request.
    /// </para>
    /// <para>
    /// Note that this method performs reflection at runtime and may not work in Native AOT scenarios. For
    /// Native AOT compatibility, consider using the generic <see cref="WithPrompts{TPromptType}"/> method instead.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>
    /// Register prompts from the current assembly:
    /// <code>
    /// // In a class library with prompt classes
    /// services.AddMcpServer()
    ///     .WithPromptsFromAssembly();
    /// </code>
    /// </para>
    /// <para>
    /// Register prompts from a specific assembly:
    /// <code>
    /// // Load prompts from a specific assembly
    /// services.AddMcpServer()
    ///     .WithPromptsFromAssembly(typeof(ExternalPrompts).Assembly);
    /// </code>
    /// </para>
    /// </example>
    /// <seealso cref="WithPrompts{TPromptType}"/>
    /// <seealso cref="WithPrompts(IMcpServerBuilder, IEnumerable{Type})"/>
    /// <seealso cref="McpServerPromptTypeAttribute"/>
    /// <seealso cref="McpServerPromptAttribute"/>
    [RequiresUnreferencedCode(WithPromptsRequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithPromptsFromAssembly(this IMcpServerBuilder builder, Assembly? promptAssembly = null)
    {
        Throw.IfNull(builder);

        promptAssembly ??= Assembly.GetCallingAssembly();

        return builder.WithPrompts(
            from t in promptAssembly.GetTypes()
            where t.GetCustomAttribute<McpServerPromptTypeAttribute>() is not null
            select t);
    }
    #endregion

    #region Handlers
    /// <summary>
    /// Configures a handler for listing resource templates in the Model Context Protocol server.
    /// This handler is responsible for providing clients with information about available resource templates
    /// that can be used to construct resource URIs.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <param name="handler">The handler function that processes resource template list requests.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Resource templates describe the structure of resource URIs that the server can handle. They include
    /// URI templates (according to RFC 6570) that clients can use to construct valid resource URIs.
    /// </para>
    /// <para>
    /// This handler is typically paired with <see cref="WithReadResourceHandler"/> to provide a complete
    /// resource system where templates define the URI patterns and the read handler provides the actual content.
    /// </para>
    /// <para>
    /// The <see cref="ResourceTemplate"/> objects returned by this handler should include:
    /// <list type="bullet">
    ///   <item><description>A required <c>Name</c> property that provides a human-readable label for the template</description></item>
    ///   <item><description>A required <c>UriTemplate</c> that defines the pattern for constructing resource URIs</description></item>
    ///   <item><description>An optional <c>Description</c> that explains the purpose of the resource template</description></item>
    ///   <item><description>An optional <c>MimeType</c> that indicates the content type of resources created from this template</description></item>
    ///   <item><description>Optional <c>Annotations</c> that can specify intended audience and priority</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddMcpServer()
    ///     .WithListResourceTemplatesHandler((ctx, ct) =>
    ///     {
    ///         return Task.FromResult(new ListResourceTemplatesResult
    ///         {
    ///             ResourceTemplates =
    ///             [
    ///                 new ResourceTemplate { 
    ///                     Name = "Static Resource", 
    ///                     Description = "A static resource with a numeric ID", 
    ///                     UriTemplate = "test://static/resource/{id}" 
    ///                 },
    ///                 new ResourceTemplate {
    ///                     Name = "Document",
    ///                     Description = "A document resource containing formatted text content",
    ///                     UriTemplate = "document://{documentType}/{id}",
    ///                     MimeType = "application/pdf"
    ///                 }
    ///             ]
    ///         });
    ///     });
    /// </code>
    /// </example>
    /// <seealso cref="ResourceTemplate"/>
    /// <seealso cref="ListResourceTemplatesResult"/>
    /// <seealso cref="WithReadResourceHandler"/>
    public static IMcpServerBuilder WithListResourceTemplatesHandler(this IMcpServerBuilder builder, Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, Task<ListResourceTemplatesResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListResourceTemplatesHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for list tools requests. This handler is responsible for providing clients with
    /// information about the tools available for invocation through the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler that processes list tools requests. The handler receives the request context and cancellation token,
    /// and should return a <see cref="ListToolsResult"/> with available tools.</param>
    /// <returns>The builder instance to enable method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This handler is called when a client requests a list of available tools. It should return all tools
    /// that can be invoked through the server, including their names, descriptions, and parameter specifications.
    /// The handler can optionally support pagination via the cursor mechanism for large tool collections.
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
    /// <example>
    /// <code>
    /// services.AddMcpServer()
    ///     .WithListToolsHandler(async (request, cancellationToken) =>
    ///     {
    ///         return new ListToolsResult
    ///         {
    ///             Tools = new List&lt;Tool&gt;
    ///             {
    ///                 new Tool
    ///                 {
    ///                     Name = "echo",
    ///                     Description = "Echoes back the provided message",
    ///                     Parameters = new ToolParameters
    ///                     {
    ///                         Type = "object",
    ///                         Properties = new Dictionary&lt;string, ToolParameterProperty&gt;
    ///                         {
    ///                             ["message"] = new ToolParameterProperty
    ///                             {
    ///                                 Type = "string",
    ///                                 Description = "The message to echo back"
    ///                             }
    ///                         },
    ///                         Required = ["message"]
    ///                     }
    ///                 },
    ///                 new Tool
    ///                 {
    ///                     Name = "calculator",
    ///                     Description = "Performs basic arithmetic operations",
    ///                     Parameters = new ToolParameters
    ///                     {
    ///                         Type = "object",
    ///                         Properties = new Dictionary&lt;string, ToolParameterProperty&gt;
    ///                         {
    ///                             ["operation"] = new ToolParameterProperty
    ///                             {
    ///                                 Type = "string",
    ///                                 Description = "The operation to perform (add, subtract, multiply, divide)",
    ///                                 Enum = ["add", "subtract", "multiply", "divide"]
    ///                             },
    ///                             ["a"] = new ToolParameterProperty { Type = "number", Description = "First operand" },
    ///                             ["b"] = new ToolParameterProperty { Type = "number", Description = "Second operand" }
    ///                         },
    ///                         Required = ["operation", "a", "b"]
    ///                     }
    ///                 }
    ///             }
    ///         };
    ///     });
    /// </code>
    /// </example>
    /// <seealso cref="WithCallToolHandler"/>
    /// <seealso cref="Tool"/>
    /// <seealso cref="ListToolsResult"/>
    public static IMcpServerBuilder WithListToolsHandler(this IMcpServerBuilder builder, Func<RequestContext<ListToolsRequestParams>, CancellationToken, Task<ListToolsResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListToolsHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for call tool requests. This handler is invoked when a client makes a call to a tool
    /// that isn't found in the <see cref="McpServerTool"/> collection.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes tool calls. The handler receives the request context and cancellation token,
    /// and should return a <see cref="CallToolResponse"/> with the execution results.</param>
    /// <returns>The builder instance for chaining.</returns>
    /// <remarks>
    /// The call tool handler is responsible for executing custom tools and returning their results to clients.
    /// When a tool is called that isn't registered in the <see cref="McpServerTool"/> collection, this handler is invoked.
    /// 
    /// This method is typically paired with <see cref="WithListToolsHandler"/> to provide a complete tools implementation,
    /// where <see cref="WithListToolsHandler"/> advertises available tools and this handler executes them.
    /// 
    /// The handler should handle errors gracefully by either:
    /// <list type="bullet">
    ///   <item>Throwing an exception which will be properly communicated to the client</item>
    ///   <item>Returning a <see cref="CallToolResponse"/> with its <see cref="CallToolResponse.IsError"/> property set to true</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddMcpServer()
    ///     .WithCallToolHandler(async (request, cancellationToken) =>
    ///     {
    ///         if (request.Params?.Name == "echo")
    ///         {
    ///             var message = request.Params?.Arguments?["message"].ToString();
    ///             return new CallToolResponse()
    ///             {
    ///                 Content = [new Content() { Text = "Echo: " + message, Type = "text" }]
    ///             };
    ///         }
    ///         else if (request.Params?.Name == "calculator")
    ///         {
    ///             var operation = request.Params?.Arguments?["operation"].ToString();
    ///             var a = Convert.ToDouble(request.Params?.Arguments?["a"]);
    ///             var b = Convert.ToDouble(request.Params?.Arguments?["b"]);
    ///             
    ///             double result = operation switch
    ///             {
    ///                 "add" => a + b,
    ///                 "subtract" => a - b,
    ///                 "multiply" => a * b,
    ///                 "divide" => a / b,
    ///                 _ => throw new Exception($"Unknown operation: {operation}")
    ///             };
    ///             
    ///             return new CallToolResponse()
    ///             {
    ///                 Content = [new Content() { Text = result.ToString(), Type = "text" }]
    ///             };
    ///         }
    ///         
    ///         throw new Exception($"Unknown tool: {request.Params?.Name}");
    ///     });
    /// </code>
    /// </example>
    public static IMcpServerBuilder WithCallToolHandler(this IMcpServerBuilder builder, Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<CallToolResponse>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.CallToolHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for list prompts requests. Registers a function that will be called when clients
    /// request a list of available prompts from the server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler that returns available prompts. The handler can support pagination
    /// through the cursor parameter in the request.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// This handler is used alongside any prompts defined in <see cref="PromptsCapability.PromptCollection"/>,
    /// combining results from both sources when returning prompts to clients.
    /// </remarks>
    public static IMcpServerBuilder WithListPromptsHandler(this IMcpServerBuilder builder, Func<RequestContext<ListPromptsRequestParams>, CancellationToken, Task<ListPromptsResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListPromptsHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for get prompt requests. This handler is invoked when a client requests details
    /// for a specific prompt that isn't found in the <see cref="McpServerPrompt"/> collection.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes prompt requests. The handler receives the request context containing 
    /// the prompt name and arguments, and a cancellation token, and should return a <see cref="GetPromptResult"/> with the prompt details.</param>
    /// <summary>
    /// Sets the handler for get prompt requests. This enables dynamic prompt resolution based on
    /// the prompt name and arguments provided in the request.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    /// <example>
    /// <code>
    /// var builder = new McpServerBuilder()
    ///     .WithGetPromptHandler(async (request, cancellationToken) =>
    ///     {
    ///         if (request.Params?.Name == "simple_prompt")
    ///         {
    ///             return new GetPromptResult
    ///             {
    ///                 Messages = new List&lt;PromptMessage&gt;
    ///                 {
    ///                     new PromptMessage
    ///                     {
    ///                         Role = Role.User,
    ///                         Content = new Content
    ///                         {
    ///                             Type = "text",
    ///                             Text = "This is a simple prompt without arguments."
    ///                         }
    ///                     }
    ///                 }
    ///             };
    ///         }
    ///         
    ///         throw new Exception($"Unknown prompt: {request.Params?.Name}");
    ///     });
    /// </code>
    /// </example>
    public static IMcpServerBuilder WithGetPromptHandler(this IMcpServerBuilder builder, Func<RequestContext<GetPromptRequestParams>, CancellationToken, Task<GetPromptResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.GetPromptHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for list resources requests. This handler is responsible for providing
    /// a list of available resources that clients can access through the Model Context Protocol server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes resource list requests. The handler receives
    /// the request context and cancellation token, and should return a <see cref="ListResourcesResult"/> with available resources.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This handler is typically paired with <see cref="WithReadResourceHandler"/> to provide a complete resources implementation,
    /// where this handler advertises available resources and the read handler provides their content when requested.
    /// </para>
    /// <para>
    /// If a <see cref="WithReadResourceHandler"/> is registered without a corresponding <see cref="WithListResourcesHandler"/>,
    /// the server will throw an exception during startup.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddMcpServer()
    ///     .WithListResourcesHandler((ctx, ct) =>
    ///     {
    ///         return Task.FromResult(new ListResourcesResult
    ///         {
    ///             Resources =
    ///             [
    ///                 new Resource { 
    ///                     Name = "Documentation", 
    ///                     Description = "API documentation in markdown format", 
    ///                     Uri = "resource://docs/api.md" 
    ///                 },
    ///                 new Resource { 
    ///                     Name = "Configuration", 
    ///                     Description = "System configuration file", 
    ///                     Uri = "resource://config/system.json" 
    ///                 }
    ///             ]
    ///         });
    ///     });
    /// </code>
    /// </example>
    public static IMcpServerBuilder WithListResourcesHandler(this IMcpServerBuilder builder, Func<RequestContext<ListResourcesRequestParams>, CancellationToken, Task<ListResourcesResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListResourcesHandler = handler);
        return builder;
    }

    /// <summary>
    /// Registers a handler that will be invoked when clients request to read a specific resource.
    /// This handler is responsible for locating and returning the contents of resources identified by URIs.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler that will be invoked when a client requests to read a resource.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This handler is typically paired with <see cref="WithListResourcesHandler"/> to provide a complete resources implementation,
    /// where the list handler advertises available resources and the read handler provides their content when requested.
    /// </para>
    /// <para>
    /// If a <see cref="WithReadResourceHandler"/> is registered without a corresponding <see cref="WithListResourcesHandler"/>,
    /// the server will throw an exception during startup.
    /// </para>
    /// <para>
    /// The handler receives a context containing the resource URI and should return a <see cref="ReadResourceResult"/> with 
    /// the resource contents, which can be either text or binary data.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// .WithReadResourceHandler((ctx, ct) =>
    /// {
    ///     var uri = ctx.Params?.Uri;
    ///     
    ///     if (uri is null || !uri.StartsWith("test://static/resource/"))
    ///     {
    ///         throw new NotSupportedException($"Unknown resource: {uri}");
    ///     }
    ///     
    ///     int id = int.Parse(uri["test://static/resource/".Length..]);
    ///     
    ///     return Task.FromResult(new ReadResourceResult
    ///     {
    ///         Contents = 
    ///         [
    ///             new TextResourceContents
    ///             {
    ///                 Uri = uri,
    ///                 MimeType = "text/plain",
    ///                 Text = $"This is static resource {id}"
    ///             }
    ///         ]
    ///     });
    /// })
    /// </code>
    /// </example>
    /// <seealso cref="WithListResourcesHandler"/>
    public static IMcpServerBuilder WithReadResourceHandler(this IMcpServerBuilder builder, Func<RequestContext<ReadResourceRequestParams>, CancellationToken, Task<ReadResourceResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ReadResourceHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for completion requests. This handler provides auto-completion suggestions
    /// for prompt arguments or resource references when clients request help with parameter values.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes completion requests. The handler receives
    /// a request context with reference information and the current argument, and returns appropriate completion suggestions.</param>
    /// <returns>The builder instance for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The completion handler is invoked when clients request suggestions for argument values while
    /// users are typing. This enables autocomplete functionality for both prompt arguments and resource references.
    /// </para>
    /// <para>
    /// The handler receives a <see cref="RequestContext{CompleteRequestParams}"/> containing:
    /// <list type="bullet">
    ///   <item><description>The <c>Ref</c> the client is referencing (prompt, resource, etc.)</description></item>
    ///   <item><description>The <c>Argument</c> the client is requesting completions for, including its name and current value</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The handler should return a <see cref="CompleteResult"/> with suggested completions filtered
    /// based on what the user has already typed. For large sets of completions, pagination can be
    /// supported through the <see cref="Completion.HasMore"/> and <see cref="Completion.Total"/> properties.
    /// </para>
    /// <para>
    /// It's recommended to handle different reference types appropriately (e.g., "ref/prompt" vs "ref/resource"),
    /// as they may require different completion logic.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// .WithCompleteHandler((ctx, ct) =>
    /// {
    ///     var exampleCompletions = new Dictionary&lt;string, IEnumerable&lt;string&gt;&gt;
    ///     {
    ///         { "style", ["casual", "formal", "technical", "friendly"] },
    ///         { "temperature", ["0", "0.5", "0.7", "1.0"] },
    ///         { "resourceId", ["1", "2", "3", "4", "5"] }
    ///     };
    ///
    ///     if (ctx.Params is not { } @params)
    ///     {
    ///         throw new NotSupportedException($"Params are required.");
    ///     }
    ///     
    ///     var @ref = @params.Ref;
    ///     var argument = @params.Argument;
    ///
    ///     if (@ref.Type == "ref/resource")
    ///     {
    ///         var resourceId = @ref.Uri?.Split("/").Last();
    ///
    ///         if (resourceId is null)
    ///         {
    ///             return Task.FromResult(new CompleteResult());
    ///         }
    ///
    ///         var values = exampleCompletions["resourceId"].Where(id => id.StartsWith(argument.Value));
    ///
    ///         return Task.FromResult(new CompleteResult
    ///         {
    ///             Completion = new Completion { Values = [..values], HasMore = false, Total = values.Count() }
    ///         });
    ///     }
    ///
    ///     if (@ref.Type == "ref/prompt")
    ///     {
    ///         if (!exampleCompletions.TryGetValue(argument.Name, out IEnumerable&lt;string&gt;? value))
    ///         {
    ///             throw new NotSupportedException($"Unknown argument name: {argument.Name}");
    ///         }
    ///
    ///         var values = value.Where(value => value.StartsWith(argument.Value));
    ///         return Task.FromResult(new CompleteResult
    ///         {
    ///             Completion = new Completion { Values = [..values], HasMore = false, Total = values.Count() }
    ///         });
    ///     }
    ///
    ///     throw new NotSupportedException($"Unknown reference type: {@ref.Type}");
    /// })
    /// </code>
    /// </example>
    /// <seealso cref="CompleteResult"/>
    /// <seealso cref="CompleteRequestParams"/>
    /// <seealso cref="Completion"/>
    public static IMcpServerBuilder WithCompleteHandler(this IMcpServerBuilder builder, Func<RequestContext<CompleteRequestParams>, CancellationToken, Task<CompleteResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.CompleteHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for subscribe to resources requests. This handler is invoked when clients request
    /// to be notified about changes to specific resources identified by their URIs.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes resource subscription requests. The handler receives
    /// the request context containing the resource URI and a cancellation token, and returns an <see cref="EmptyResult"/>
    /// upon successful registration of the subscription.</param>
    /// <returns>The builder instance for method chaining.</returns>
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
    /// <example>
    /// <code>
    /// HashSet&lt;string&gt; subscriptions = [];
    /// 
    /// services.AddMcpServer()
    ///     .WithSubscribeToResourcesHandler(async (ctx, ct) =>
    ///     {
    ///         var uri = ctx.Params?.Uri;
    ///         
    ///         if (uri is not null)
    ///         {
    ///             subscriptions.Add(uri);
    ///             
    ///             // Optionally notify client of successful subscription
    ///             await ctx.Server.SendNotificationAsync(
    ///                 "resource/updated",
    ///                 new { Uri = uri, Status = "Subscription activated" },
    ///                 cancellationToken: ct);
    ///         }
    ///         
    ///         return new EmptyResult();
    ///     });
    /// </code>
    /// </example>
    /// <seealso cref="WithUnsubscribeFromResourcesHandler"/>
    /// <seealso cref="WithReadResourceHandler"/>
    /// <seealso cref="WithListResourcesHandler"/>
    /// <seealso cref="EmptyResult"/>
    public static IMcpServerBuilder WithSubscribeToResourcesHandler(this IMcpServerBuilder builder, Func<RequestContext<SubscribeRequestParams>, CancellationToken, Task<EmptyResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.SubscribeToResourcesHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for unsubscribe from resources requests. This handler is invoked when clients request
    /// to cancel notifications about changes to specific resources identified by their URIs.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler function that processes resource unsubscription requests. The handler receives
    /// the request context containing the resource URI and a cancellation token, and returns an <see cref="EmptyResult"/>
    /// upon successful cancellation of the subscription.</param>
    /// <returns>The builder instance for method chaining.</returns>
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
    /// to the specified resource. This helps conserve resources and network bandwidth.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// HashSet&lt;string&gt; subscriptions = [];
    /// 
    /// services.AddMcpServer()
    ///     .WithUnsubscribeFromResourcesHandler((ctx, ct) =>
    ///     {
    ///         var uri = ctx.Params?.Uri;
    ///         if (uri is not null)
    ///         {
    ///             subscriptions.Remove(uri);
    ///         }
    ///         return Task.FromResult(new EmptyResult());
    ///     });
    /// </code>
    /// </example>
    /// <seealso cref="WithSubscribeToResourcesHandler"/>
    /// <seealso cref="WithReadResourceHandler"/>
    /// <seealso cref="WithListResourcesHandler"/>
    /// <seealso cref="EmptyResult"/>
    public static IMcpServerBuilder WithUnsubscribeFromResourcesHandler(this IMcpServerBuilder builder, Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, Task<EmptyResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.UnsubscribeFromResourcesHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for processing logging level change requests from clients.
    /// </summary>
    /// <param name="builder">The server builder instance.</param>
    /// <param name="handler">The handler that processes requests to change the logging level.</param>
    /// <returns>The server builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// When a client sends a <c>logging/setLevel</c> request, this handler will be invoked to process
    /// the requested level change. The server typically adjusts its internal logging level threshold
    /// and may begin sending log messages at or above the specified level to the client.
    /// </para>
    /// <para>
    /// This handler is required when the server has enabled the <see cref="LoggingCapability"/>. If the capability
    /// is enabled but no handler is provided, a <see cref="Exception"/> will be thrown when the server starts.
    /// </para>
    /// <para>
    /// The handler receives a <see cref="RequestContext{SetLevelRequestParams}"/> containing the client's requested 
    /// logging level and should return an <see cref="EmptyResult"/> when successful.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Add logging capability to the server
    /// var _minimumLoggingLevel = LoggingLevel.Warning; // Default level
    /// 
    /// builder.WithSetLoggingLevelHandler(async (ctx, ct) =>
    /// {
    ///     // Update the internal logging level threshold
    ///     _minimumLoggingLevel = ctx.Params.Level;
    ///     
    ///     // Optionally confirm the change with a notification
    ///     await ctx.Server.SendNotificationAsync(
    ///         NotificationMethods.LoggingMessageNotification,
    ///         new LoggingMessageNotificationParams
    ///         {
    ///             Level = LoggingLevel.Info,
    ///             Logger = "server",
    ///             Data = JsonSerializer.SerializeToElement($"Logging level set to {_minimumLoggingLevel}")
    ///         });
    ///     
    ///     return new EmptyResult();
    /// });
    /// </code>
    /// </example>
    /// <seealso cref="LoggingCapability"/>
    /// <seealso cref="SetLevelRequestParams"/>
    /// <seealso cref="LoggingLevel"/>
    public static IMcpServerBuilder WithSetLoggingLevelHandler(this IMcpServerBuilder builder, Func<RequestContext<SetLevelRequestParams>, CancellationToken, Task<EmptyResult>> handler)
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
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method configures the server to communicate using the standard input and output streams,
    /// which is commonly used when the Model Context Protocol server needs to communicate with another process
    /// or when building command-line applications that integrate with other tools.
    /// </para>
    /// <para>
    /// When using this transport, the server runs as a single-session service that exits when the
    /// stdin stream is closed. This makes it suitable for scenarios where the server should terminate
    /// when the parent process disconnects.
    /// </para>
    /// <para>
    /// The method automatically adds the necessary services to the dependency injection container,
    /// including a <see cref="SingleSessionMcpServerHostedService"/> that manages the server's lifecycle.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Configure an MCP server using stdin/stdout communication
    /// var builder = Host.CreateApplicationBuilder(args);
    /// 
    /// builder.Services.AddMcpServer()
    ///     .WithStdioServerTransport()
    ///     .WithTools&lt;MyTools&gt;();
    /// 
    /// // Optional: Configure logging to stderr to avoid interfering with stdout protocol messages
    /// builder.Logging.AddConsole(options =>
    /// {
    ///     options.LogToStandardErrorThreshold = LogLevel.Trace;
    /// });
    /// 
    /// await builder.Build().RunAsync();
    /// </code>
    /// </example>
    /// <seealso cref="WithStreamServerTransport"/>
    /// <seealso cref="StdioServerTransport"/>
    /// <seealso cref="SingleSessionMcpServerHostedService"/>
    public static IMcpServerBuilder WithStdioServerTransport(this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);

        AddSingleSessionServerDependencies(builder.Services);
        builder.Services.AddSingleton<ITransport, StdioServerTransport>();

        return builder;
    }

    /// <summary>
    /// Adds a server transport that uses the specified input and output streams for communication.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="inputStream">The input <see cref="Stream"/> to use as standard input.</param>
    /// <param name="outputStream">The output <see cref="Stream"/> to use as standard output.</param>
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
}
