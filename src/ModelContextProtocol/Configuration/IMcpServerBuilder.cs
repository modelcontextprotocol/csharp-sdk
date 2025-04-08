using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Builder for configuring <see cref="ModelContextProtocol.Server.IMcpServer"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// The IMcpServerBuilder interface provides a fluent API for configuring Model Context Protocol (MCP) servers
/// when using dependency injection. It exposes methods for registering tools, prompts, custom request handlers,
/// and server transports, allowing for comprehensive server configuration through a chain of method calls.
/// </para>
/// <para>
/// The builder is obtained from the <see cref="McpServerServiceCollectionExtensions.AddMcpServer"/> extension 
/// method and provides access to the underlying service collection via the <see cref="Services"/> property.
/// </para>
/// <para>
/// Example usage in a basic scenario:
/// <code>
/// // In ConfigureServices method
/// services.AddMcpServer(options => 
/// {
///     options.ServerCapabilities = new ServerCapabilities
///     {
///         // Configure server capabilities
///     };
/// })
/// .WithTools&lt;MyToolsClass&gt;()
/// .WithPrompts&lt;MyPromptsClass&gt;()
/// .WithStdioServerTransport();
/// 
/// // Then inject IMcpServer where needed
/// public class MyService(IMcpServer server)
/// {
///     // Use the server
/// }
/// </code>
/// </para>
/// <para>
/// Example with multiple tool classes and a console application:
/// <code>
/// var builder = Host.CreateApplicationBuilder(args);
/// builder.Services.AddMcpServer()
///     .WithStdioServerTransport()
///     .WithTools&lt;EchoTool&gt;()
///     .WithTools&lt;CalculatorTool&gt;()
///     .WithTools&lt;WeatherTool&gt;()
///     .WithPrompts&lt;SimplePromptType&gt;();
///     
/// var app = builder.Build();
/// await app.RunAsync();
/// </code>
/// </para>
/// <para>
/// Example in an ASP.NET Core application:
/// <code>
/// var builder = WebApplication.CreateBuilder(args);
/// builder.Services.AddMcpServer()
///     .WithTools&lt;EchoTool&gt;()
///     .WithTools&lt;SampleLlmTool&gt;();
///     
/// var app = builder.Build();
/// app.MapMcp();
/// app.Run();
/// </code>
/// </para>
/// </remarks>
public interface IMcpServerBuilder
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
