using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;
using Microsoft.Extensions.Logging;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a factory for creating <see cref="IMcpServer"/> instances.
/// This is the recommended way to create MCP server instances rather than directly instantiating the server class.
/// The factory handles proper initialization of server instances with the required dependencies.
/// </summary>
public static class McpServerFactory
{
    /// <summary>
    /// Creates a new instance of the <see cref="IMcpServer"/> class.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established MCP session.</param>
    /// <param name="serverOptions">
    /// Configuration options for this server, including capabilities. 
    /// Make sure to accurately reflect exactly what capabilities the server supports and does not support.
    /// </param>
    /// <param name="loggerFactory">Logger factory to use for logging. If null, logging will be disabled.</param>
    /// <param name="serviceProvider">Optional service provider to create new instances of tools and other dependencies.</param>
    /// <returns>An <see cref="IMcpServer"/> instance that should be disposed when no longer needed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// // Create a transport (implementation depends on your communication channel)
    /// var transport = new YourTransportImplementation();
    /// 
    /// // Create server options with tools and prompts
    /// var options = new McpServerOptions
    /// {
    ///     ProtocolVersion = "1.0",
    ///     InitializationTimeout = TimeSpan.FromSeconds(30),
    ///     ServerCapabilities = new()
    ///     {
    ///         ToolsCollection = new()
    ///         {
    ///             McpServerTool.Create((string city) => $"The weather in {city} is sunny.", 
    ///                 new() { Name = "GetWeather", Description = "Gets the weather for a city." })
    ///         },
    ///         PromptsCollection = new()
    ///         {
    ///             McpServerPrompt.Create(() => "What is the weather in Paris?",
    ///                 new() { Name = "WeatherPrompt" })
    ///         }
    ///     }
    /// };
    /// 
    /// // Create and use the server with proper disposal
    /// await using var server = McpServerFactory.Create(transport, options, loggerFactory);
    /// await server.RunAsync(cancellationToken); // Process the MCP session
    /// </code>
    /// </example>
    public static IMcpServer Create(
        ITransport transport,
        McpServerOptions serverOptions,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null)
    {
        Throw.IfNull(transport);
        Throw.IfNull(serverOptions);

        return new McpServer(transport, serverOptions, loggerFactory, serviceProvider);
    }
}
