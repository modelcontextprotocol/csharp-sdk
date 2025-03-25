using ModelContextProtocol.Configuration;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol;

/// <summary>
/// Extension to configure the MCP server with tools
/// </summary>
public static partial class McpServerBuilderExtensions
{
    private const string RequiresUnreferencedCodeMessage = "This method requires dynamic lookup of method metadata and might not work in Native AOT.";

    /// <summary>
    /// Adds a tool to the server.
    /// </summary>
    /// <typeparam name="TTool">The tool type.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMcpServerBuilder WithTools<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] TTool>(
        this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);

        foreach (var toolMethod in GetToolMethods(typeof(TTool)))
        {
            builder.Services.AddSingleton(services => McpServerTool.Create(toolMethod, services: services));
        }

        return builder;
    }

    /// <summary>
    /// Adds tools to the server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="toolTypes">Types with marked methods to add as tools to the server.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="toolTypes"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithTools(this IMcpServerBuilder builder, params IEnumerable<Type> toolTypes)
    {
        Throw.IfNull(builder);
        Throw.IfNull(toolTypes);

        foreach (var toolType in toolTypes)
        {
            if (toolType is not null)
            {
                foreach (var toolMethod in GetToolMethods(toolType))
                {
                    builder.Services.AddSingleton(services => McpServerTool.Create(toolMethod, services: services));
                }
            }
        }
        
        return builder;
    }

    /// <summary>
    /// Adds types marked with the <see cref="McpServerToolTypeAttribute"/> attribute from the given assembly as tools to the server.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="toolAssembly">The assembly to load the types from. Null to get the current assembly</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    public static IMcpServerBuilder WithToolsFromAssembly(this IMcpServerBuilder builder, Assembly? toolAssembly = null)
    {
        Throw.IfNull(builder);

        toolAssembly ??= Assembly.GetCallingAssembly();

        return builder.WithTools(
            from t in toolAssembly.GetTypes()
            where t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
            select t);
    }

    private static IEnumerable<MethodInfo> GetToolMethods(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type toolType) =>
        from method in toolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        where method.GetCustomAttribute<McpServerToolAttribute>() is not null
        select method;
}
