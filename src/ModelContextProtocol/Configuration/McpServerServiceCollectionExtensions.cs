using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring MCP server integration with dependency injection.
/// </summary>
/// <remarks>
/// <para>
/// These extension methods provide a simple way to register and configure an MCP server
/// within an application's dependency injection container. The primary method, <see cref="AddMcpServer"/>,
/// registers the necessary services and returns an <see cref="IMcpServerBuilder"/> that can be used
/// to further configure the server through a fluent API.
/// </para>
/// <para>
/// This integration simplifies the setup of MCP servers in ASP.NET Core applications, hosted services,
/// or any application that uses the Microsoft.Extensions.DependencyInjection container.
/// </para>
/// </remarks>
public static class McpServerServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Model Context Protocol (MCP) server to the service collection with default options.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the server to.</param>
    /// <param name="configureOptions">Optional callback to configure the <see cref="McpServerOptions"/>.</param>
    /// <returns>An <see cref="IMcpServerBuilder"/> that can be used to further configure the MCP server.</returns>
    /// <example>
    /// <code>
    /// // In a Web application:
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddMcpServer()
    ///     .WithStdioServerTransport()
    ///     .WithTools&lt;EchoTool&gt;()
    ///     .WithTools&lt;SampleLlmTool&gt;();
    /// 
    /// var app = builder.Build();
    /// app.MapMcp();
    /// app.Run();
    /// 
    /// // In a Host application:
    /// var builder = Host.CreateApplicationBuilder(args);
    /// builder.Services
    ///     .AddMcpServer()
    ///     .WithStdioServerTransport()
    ///     .WithTools&lt;AddTool&gt;()
    ///     .WithTools&lt;EchoTool&gt;()
    ///     .WithPrompts&lt;SimplePromptType&gt;();
    /// 
    /// var host = builder.Build();
    /// await host.RunAsync();
    /// </code>
    /// </example>
    public static IMcpServerBuilder AddMcpServer(this IServiceCollection services, Action<McpServerOptions>? configureOptions = null)
    {
        services.AddOptions();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<McpServerOptions>, McpServerOptionsSetup>());
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        return new DefaultMcpServerBuilder(services);
    }
}
