using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides a builder for configuring <see cref="McpServer"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IMcpServerBuilder"/> interface provides a fluent API for configuring Model Context Protocol (MCP) servers
/// when using dependency injection. It exposes methods for registering tools, prompts, custom request handlers,
/// and server transports, allowing for comprehensive server configuration through a chain of method calls.
/// </para>
/// <para>
/// The builder is obtained from the <see cref="McpServerServiceCollectionExtensions.AddMcpServer"/> extension
/// method and provides access to the underlying service collection via the <see cref="Services"/> property.
/// </para>
/// </remarks>
public interface IMcpServerBuilder
{
    /// <summary>
    /// Gets the associated service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
