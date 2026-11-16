using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.Tests.Utils;

/// <summary>
/// Creates clients for controlled test servers that respond to discovery, including with a fallback error.
/// Do not use these defaults with servers that silently ignore discovery or in discovery-timeout tests.
/// </summary>
internal static class TestMcpClient
{
    public static McpClientOptions CreateOptions() => new()
    {
        DiscoverProbeTimeout = Timeout.InfiniteTimeSpan,
        InitializationTimeout = TestConstants.DefaultTimeout,
    };

    public static Task<McpClient> CreateAsync(
        IClientTransport clientTransport,
        McpClientOptions? clientOptions = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default) =>
        McpClient.CreateAsync(clientTransport, clientOptions ?? CreateOptions(), loggerFactory, cancellationToken);
}
