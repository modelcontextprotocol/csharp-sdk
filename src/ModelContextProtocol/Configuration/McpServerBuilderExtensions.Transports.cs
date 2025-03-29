using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Configuration;
using ModelContextProtocol.Hosting;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol;

/// <summary>
/// Extension to configure the MCP server with transports
/// </summary>
public static partial class McpServerBuilderExtensions
{
    /// <summary>
    /// Adds a server transport that uses stdin/stdout for communication.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    public static IMcpServerBuilder WithStdioServerTransport(this IMcpServerBuilder builder)
    {
        return builder.WithServerTransport<StdioServerTransport>();
    }

    /// <summary>
    /// Adds a server transport that uses SSE via a HttpListener for communication.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    public static IMcpServerBuilder WithHttpListenerSseServerTransport(this IMcpServerBuilder builder)
    {
        return builder.WithServerTransport<HttpListenerSseServerTransport>();
    }

    /// <summary>
    /// Adds a server transport for in-memory communication.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    public static IMcpServerBuilder WithInMemoryServerTransport(this IMcpServerBuilder builder)
    {
        return builder.WithServerTransport<InMemoryServerTransport>();
    }

    /// <summary>
    /// Adds a server transport for in-memory communication.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handleMessageDelegate">Delegate to handle messages.</param>
    public static IMcpServerBuilder WithInMemoryServerTransport(this IMcpServerBuilder builder, Func<IJsonRpcMessage, CancellationToken, Task<IJsonRpcMessage?>> handleMessageDelegate)
    {
        var transport = new InMemoryServerTransport
        {
            HandleMessage = handleMessageDelegate
        };

        return builder.WithServerTransport(transport);
    }

    /// <summary>
    /// Adds a server transport for communication.
    /// </summary>
    /// <typeparam name="TTransport">The type of the server transport to use.</typeparam>
    /// <param name="builder">The builder instance.</param>
    public static IMcpServerBuilder WithServerTransport<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTransport>(this IMcpServerBuilder builder) where TTransport : class, IServerTransport
    {
        Throw.IfNull(builder);

        builder.Services.AddSingleton<IServerTransport, TTransport>();
        builder.Services.AddHostedService<McpServerHostedService>();
        return builder;
    }

    /// <summary>
    /// Adds a server transport for communication.
    /// </summary>
    /// <param name="serverTransport">Instance of the server transport.</param>
    /// <param name="builder">The builder instance.</param>
    public static IMcpServerBuilder WithServerTransport(this IMcpServerBuilder builder, IServerTransport serverTransport)
    {
        Throw.IfNull(builder);

        builder.Services.AddSingleton<IServerTransport>(serverTransport);
        builder.Services.AddHostedService<McpServerHostedService>();
        return builder;
    }
}
