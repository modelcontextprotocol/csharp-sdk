using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Configuration;
using ModelContextProtocol.Hosting;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol;

/// <summary>
/// Extension to configure the MCP server with transports
/// </summary>
public static partial class McpServerBuilderExtensions
{
    /// <summary>
    /// Adds a server transport that uses in memory communication.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    public static IMcpServerBuilder WithInMemoryServerTransport(this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);
        var (clientTransport, serverTransport) = InMemoryTransport.Create();
        builder.Services.AddSingleton<IServerTransport>(serverTransport);
        builder.Services.AddSingleton<IClientTransport>(clientTransport);
        builder.Services.AddHostedService<McpServerHostedService>();
        return builder;
    }

    /// <summary>
    /// Adds a server transport that uses stdin/stdout for communication.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    public static IMcpServerBuilder WithStdioServerTransport(this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);

        builder.Services.AddSingleton<IServerTransport, StdioServerTransport>();
        builder.Services.AddHostedService<McpServerHostedService>();
        return builder;
    }

    /// <summary>
    /// Adds a server transport that uses SSE via a HttpListener for communication.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    public static IMcpServerBuilder WithHttpListenerSseServerTransport(this IMcpServerBuilder builder)
    {
        Throw.IfNull(builder);

        builder.Services.AddSingleton<IServerTransport, HttpListenerSseServerTransport>();
        builder.Services.AddHostedService<McpServerHostedService>();
        return builder;
    }
}
