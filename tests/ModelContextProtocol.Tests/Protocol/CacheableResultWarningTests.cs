// Excluded on .NET Framework: the in-memory server helper uses Stream/StreamReader async overloads
// that take a CancellationToken (e.g. StreamReader.ReadLineAsync(CancellationToken) and
// Stream.WriteAsync(ReadOnlyMemory<byte>, CancellationToken)) which are not available on net472.
#if !NET472
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Tests for the client-side SEP-2549 conformance warning: when a server that negotiated the 2026-07-28
/// (or later) protocol version returns a cacheable result (tools/list, prompts/list, resources/list,
/// resources/templates/list, resources/read) without the now-required <c>ttlMs</c>/<c>cacheScope</c>
/// fields, the client logs a warning but never throws.
/// </summary>
public class CacheableResultWarningTests : LoggedTest
{
    private const string July2026ProtocolVersion = "2026-07-28";
    private const string OlderProtocolVersion = "2025-11-25";

    public CacheableResultWarningTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    public static IEnumerable<object[]> CacheableMethods =>
    [
        [RequestMethods.ToolsList],
        [RequestMethods.PromptsList],
        [RequestMethods.ResourcesList],
        [RequestMethods.ResourcesTemplatesList],
        [RequestMethods.ResourcesRead],
    ];

    [Theory]
    [MemberData(nameof(CacheableMethods))]
    public async Task DraftServerOmittingBothHints_LogsWarning(string method)
    {
        var (call, result) = GetScenario(method, ttl: null, scope: null);

        await RunScenarioAsync(July2026ProtocolVersion, usePerRequestMetadataLifecycle: true, method, result, call, TestContext.Current.CancellationToken);

        var warning = Assert.Single(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning && m.Message.Contains(method) && m.Message.Contains("SEP-2549"));
        Assert.Contains("ttlMs", warning.Message);
        Assert.Contains("cacheScope", warning.Message);
    }

    [Fact]
    public async Task DraftServerOmittingOnlyCacheScope_WarnsAboutCacheScope()
    {
        var (call, result) = GetScenario(RequestMethods.ToolsList, ttl: TimeSpan.FromMinutes(5), scope: null);

        await RunScenarioAsync(July2026ProtocolVersion, usePerRequestMetadataLifecycle: true, RequestMethods.ToolsList, result, call, TestContext.Current.CancellationToken);

        var warning = Assert.Single(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning && m.Message.Contains("SEP-2549"));
        Assert.Contains("cacheScope", warning.Message);
        Assert.DoesNotContain("ttlMs", warning.Message);
    }

    [Fact]
    public async Task DraftServerProvidingBothHints_DoesNotWarn()
    {
        var (call, result) = GetScenario(RequestMethods.ToolsList, ttl: TimeSpan.FromMinutes(5), scope: CacheScope.Public);

        await RunScenarioAsync(July2026ProtocolVersion, usePerRequestMetadataLifecycle: true, RequestMethods.ToolsList, result, call, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning && m.Message.Contains("SEP-2549"));
    }

    [Fact]
    public async Task OlderServerOmittingHints_DoesNotWarn()
    {
        // A server on an older protocol version may legitimately omit the fields; no warning should fire.
        var (call, result) = GetScenario(RequestMethods.ToolsList, ttl: null, scope: null);

        await RunScenarioAsync(OlderProtocolVersion, usePerRequestMetadataLifecycle: false, RequestMethods.ToolsList, result, call, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning && m.Message.Contains("SEP-2549"));
    }

    [Fact]
    public async Task AutoPaginatingOverload_DraftServerOmittingHints_LogsWarning()
    {
        // The auto-paginating convenience overload calls the raw overload per page, so the warning
        // fires through that path as well.
        var result = JsonSerializer.SerializeToNode(
            new ListToolsResult { Tools = [new Tool { Name = "echo" }] },
            McpJsonUtilities.DefaultOptions)!;

        await RunScenarioAsync(
            July2026ProtocolVersion,
            usePerRequestMetadataLifecycle: true,
            RequestMethods.ToolsList,
            result,
            (c, ct) => c.ListToolsAsync(cancellationToken: ct).AsTask(),
            TestContext.Current.CancellationToken);

        Assert.Single(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning && m.Message.Contains(RequestMethods.ToolsList) && m.Message.Contains("SEP-2549"));
    }

    [Fact]
    public async Task AutoPaginatingOverload_MultiplePages_WarnsOnlyOncePerMethod()
    {
        // A non-conformant draft server omits the hints on every page. The warning must be emitted at
        // most once per method per session so that long paginated listings do not flood the log.
        const int pageCount = 4;

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var clientTask = McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream(),
                LoggerFactory),
            new McpClientOptions { ProtocolVersion = July2026ProtocolVersion },
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        var serverReader = new StreamReader(clientToServer.Reader.AsStream());
        var serverWriter = serverToClient.Writer.AsStream();

        await PerformHandshakeAsync(serverReader, serverWriter, July2026ProtocolVersion, usePerRequestMetadataLifecycle: true, TestContext.Current.CancellationToken);

        await using var client = await clientTask;

        // Respond to each tools/list page omitting the hints, advancing the cursor until the last page.
        var serverLoop = Task.Run(async () =>
        {
            int page = 0;
            while (true)
            {
                var line = await serverReader.ReadLineAsync(TestContext.Current.CancellationToken);
                if (line is null)
                {
                    return;
                }

                if (JsonSerializer.Deserialize<JsonRpcMessage>(line, McpJsonUtilities.DefaultOptions) is JsonRpcRequest request &&
                    request.Method == RequestMethods.ToolsList)
                {
                    page++;
                    var result = new ListToolsResult
                    {
                        Tools = [new Tool { Name = $"tool{page}" }],
                        NextCursor = page < pageCount ? $"page{page}" : null,
                    };

                    await WriteJsonRpcAsync(serverWriter, new JsonRpcResponse
                    {
                        Id = request.Id,
                        Result = JsonSerializer.SerializeToNode(result, McpJsonUtilities.DefaultOptions),
                    }, TestContext.Current.CancellationToken);

                    if (page >= pageCount)
                    {
                        return;
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        await serverLoop;

        Assert.Equal(pageCount, tools.Count);
        Assert.Single(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning && m.Message.Contains(RequestMethods.ToolsList) && m.Message.Contains("SEP-2549"));

        clientToServer.Writer.Complete();
        serverToClient.Writer.Complete();
    }

    private static (Func<McpClient, CancellationToken, Task> Call, JsonNode Result) GetScenario(
        string method, TimeSpan? ttl, CacheScope? scope)
    {
        var options = McpJsonUtilities.DefaultOptions;
        return method switch
        {
            RequestMethods.ToolsList => (
                (c, ct) => c.ListToolsAsync(new ListToolsRequestParams(), ct).AsTask(),
                JsonSerializer.SerializeToNode(new ListToolsResult { Tools = [], TimeToLive = ttl, CacheScope = scope }, options)!),
            RequestMethods.PromptsList => (
                (c, ct) => c.ListPromptsAsync(new ListPromptsRequestParams(), ct).AsTask(),
                JsonSerializer.SerializeToNode(new ListPromptsResult { Prompts = [], TimeToLive = ttl, CacheScope = scope }, options)!),
            RequestMethods.ResourcesList => (
                (c, ct) => c.ListResourcesAsync(new ListResourcesRequestParams(), ct).AsTask(),
                JsonSerializer.SerializeToNode(new ListResourcesResult { Resources = [], TimeToLive = ttl, CacheScope = scope }, options)!),
            RequestMethods.ResourcesTemplatesList => (
                (c, ct) => c.ListResourceTemplatesAsync(new ListResourceTemplatesRequestParams(), ct).AsTask(),
                JsonSerializer.SerializeToNode(new ListResourceTemplatesResult { ResourceTemplates = [], TimeToLive = ttl, CacheScope = scope }, options)!),
            RequestMethods.ResourcesRead => (
                (c, ct) => c.ReadResourceAsync(new ReadResourceRequestParams { Uri = "test://resource" }, ct).AsTask(),
                JsonSerializer.SerializeToNode(new ReadResourceResult { Contents = [], TimeToLive = ttl, CacheScope = scope }, options)!),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unhandled method."),
        };
    }

    private async Task RunScenarioAsync(
        string serverProtocolVersion,
        bool usePerRequestMetadataLifecycle,
        string method,
        JsonNode resultNode,
        Func<McpClient, CancellationToken, Task> clientCall,
        CancellationToken cancellationToken)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        // Pin the protocol version so the client deterministically takes the per-request metadata
        // (server/discover) lifecycle for 2026-07-28 and the initialize lifecycle for older versions.
        var clientTask = McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream(),
                LoggerFactory),
            new McpClientOptions { ProtocolVersion = serverProtocolVersion },
            loggerFactory: LoggerFactory,
            cancellationToken: cancellationToken);

        var serverReader = new StreamReader(clientToServer.Reader.AsStream());
        var serverWriter = serverToClient.Writer.AsStream();

        await PerformHandshakeAsync(serverReader, serverWriter, serverProtocolVersion, usePerRequestMetadataLifecycle, cancellationToken);

        await using var client = await clientTask;
        Assert.Equal(serverProtocolVersion, client.NegotiatedProtocolVersion);

        // Background server loop: respond to the target request with the crafted result.
        var serverLoop = Task.Run(async () =>
        {
            while (true)
            {
                var line = await serverReader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    return;
                }

                if (JsonSerializer.Deserialize<JsonRpcMessage>(line, McpJsonUtilities.DefaultOptions) is JsonRpcRequest request &&
                    request.Method == method)
                {
                    await WriteJsonRpcAsync(serverWriter, new JsonRpcResponse
                    {
                        Id = request.Id,
                        Result = resultNode,
                    }, cancellationToken);
                    return;
                }
            }
        }, cancellationToken);

        await clientCall(client, cancellationToken);
        await serverLoop;

        clientToServer.Writer.Complete();
        serverToClient.Writer.Complete();
    }

    private static async Task PerformHandshakeAsync(
        StreamReader serverReader,
        Stream serverWriter,
        string serverProtocolVersion,
        bool usePerRequestMetadataLifecycle,
        CancellationToken cancellationToken)
    {
        var requestLine = await serverReader.ReadLineAsync(cancellationToken);
        Assert.NotNull(requestLine);
        var request = JsonSerializer.Deserialize<JsonRpcRequest>(requestLine, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(request);

        if (usePerRequestMetadataLifecycle)
        {
            // Per-request metadata lifecycle (SEP-2575): no initialize handshake. The client probes
            // server/discover to learn capabilities, then sends normal RPCs carrying per-request _meta.
            Assert.Equal(RequestMethods.ServerDiscover, request.Method);

            await WriteJsonRpcAsync(serverWriter, new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToNode(new DiscoverResult
                {
                    SupportedVersions = [serverProtocolVersion],
                    Capabilities = new ServerCapabilities(),
                    ServerInfo = new Implementation { Name = "MockServer", Version = "1.0" },
                }, McpJsonUtilities.DefaultOptions),
            }, cancellationToken);
        }
        else
        {
            // Initialize handshake for older protocol versions.
            Assert.Equal(RequestMethods.Initialize, request.Method);

            await WriteJsonRpcAsync(serverWriter, new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToNode(new InitializeResult
                {
                    ProtocolVersion = serverProtocolVersion,
                    Capabilities = new ServerCapabilities(),
                    ServerInfo = new Implementation { Name = "MockServer", Version = "1.0" },
                }, McpJsonUtilities.DefaultOptions),
            }, cancellationToken);

            // Consume the initialized notification.
            var initializedLine = await serverReader.ReadLineAsync(cancellationToken);
            Assert.NotNull(initializedLine);
        }
    }

    private static async Task WriteJsonRpcAsync(Stream writer, JsonRpcMessage message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes<JsonRpcMessage>(message, McpJsonUtilities.DefaultOptions);
        await writer.WriteAsync(bytes, cancellationToken);
        await writer.WriteAsync("\n"u8.ToArray(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}

#endif
