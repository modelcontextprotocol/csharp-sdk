using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Xunit;

namespace ModelContextProtocol.Tests.Server;

// NOTE: Assumes McpServerOptions, McpServer, TestServerTransport, RequestMethods,
//       CallToolRequestParams, CallToolResult, JsonRpcMessage, JsonRpcResponse,
//       JsonRpcError, McpErrorCode, McpServerTool, Tool, RequestContext<T>,
//       TextContentBlock, McpJsonUtilities are available from project references.

/// <summary>
/// A simple test tool that simulates slow work. Used to validate timeout enforcement paths.
/// </summary>
public class SlowTool : McpServerTool, IMcpToolWithTimeout
{
    private readonly TimeSpan _workDuration;
    private readonly TimeSpan? _toolTimeout;

    public SlowTool(TimeSpan workDuration, TimeSpan? toolTimeout)
    {
        _workDuration = workDuration;
        _toolTimeout = toolTimeout;
    }

    public string Name => ProtocolTool.Name;

    /// <inheritdoc/>
    public override Tool ProtocolTool => new()
    {
        Name = "SlowTool",
        Description = "A tool that works very slowly.",
        // No input parameters; schema must be a non-null empty object.
        InputSchema = JsonDocument.Parse("""{"type": "object", "properties": {}}""").RootElement
    };

    /// <inheritdoc/>
    public override IReadOnlyList<object> Metadata => Array.Empty<object>();

    /// <inheritdoc/>
    public TimeSpan? Timeout => _toolTimeout;

    /// <summary>
    /// Simulates long-running work and cooperates with cancellation.
    /// </summary>
    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        // If the server injects a timeout-linked token, this will throw on timeout.
        await Task.Delay(_workDuration, cancellationToken);

        return new()
        {
            IsError = false, // <- explicitly success
            Content =
            [
                new TextContentBlock
                {
                    Text = $"Done after {_workDuration.TotalMilliseconds}ms."
                }
            ]
        };
    }
}

/// <summary>
/// Tests server-side tool timeout enforcement and client-initiated cancellation
/// against a live in-memory transport.
/// </summary>
public class McpServerToolTimeoutTests : LoggedTest
{
    public McpServerToolTimeoutTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper) { }

    private static McpServerOptions CreateOptions(TimeSpan? defaultTimeout = null)
        => new()
        {
            ProtocolVersion = "2024",
            InitializationTimeout = TimeSpan.FromSeconds(30),
            DefaultToolTimeout = defaultTimeout
        };

    private static async Task InitializeServerAsync(
    TestServerTransport transport,
    string? protocolVersion,
    CancellationToken ct)
    {
        var initReqId = new RequestId(Guid.NewGuid().ToString("N"));
        var initTcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnInit(JsonRpcMessage m)
        {
            if (m is JsonRpcResponse r && r.Id.ToString() == initReqId.ToString())
                initTcs.TrySetResult(r);
            if (m is JsonRpcError e && e.Id.ToString() == initReqId.ToString())
                initTcs.TrySetException(new Xunit.Sdk.XunitException(
                    $"initialize returned error. Code={e.Error.Code}, Message='{e.Error.Message}'"));
        }

        transport.OnMessageSent += OnInit;
        try
        {
            var initParams = JsonSerializer.SerializeToNode(new
            {
                protocolVersion,
                clientInfo = new { name = "ModelContextProtocol.Tests", version = "0.0.0" },
                capabilities = new { }
            }, McpJsonUtilities.DefaultOptions);

            await transport.SendMessageAsync(new JsonRpcRequest
            {
                Method = RequestMethods.Initialize,
                Id = initReqId,
                Params = initParams
            }, CancellationToken.None);

            _ = await initTcs.Task.WaitAsync(ct);
        }
        finally
        {
            transport.OnMessageSent -= OnInit;
        }

        await transport.SendMessageAsync(new JsonRpcNotification
        {
            Method = NotificationMethods.InitializedNotification,
            Params = null
        }, CancellationToken.None);
    }



    private async Task<CallToolResult> ExecuteCallToolRequest(
    McpServerOptions options,
    string toolName,
    CancellationToken externalCancellationToken = default)
    {
        // Early guard: ensure the tool exists in options.ToolCollection (clear failure if not).
        if (options?.ToolCollection is null || !options.ToolCollection.Any(t => t.ProtocolTool.Name == toolName))
            throw new Xunit.Sdk.XunitException($"Tool '{toolName}' is not registered in options.ToolCollection.");

        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, options, LoggerFactory);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            externalCancellationToken, TestContext.Current.CancellationToken);

        var runTask = server.RunAsync(linkedCts.Token);

        // MCP handshake
        await InitializeServerAsync(transport, options.ProtocolVersion, linkedCts.Token);

        var reqId = new RequestId(Guid.NewGuid().ToString("N"));

        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnReply(JsonRpcMessage m)
        {
            // Success response
            if (m is JsonRpcResponse ok && ok.Id.ToString() == reqId.ToString())
                tcs.TrySetResult(ok);

            // Protocol-level error (e.g., "tool not found", validation failures, etc.)
            if (m is JsonRpcError err && err.Id.ToString() == reqId.ToString())
                tcs.TrySetException(new Xunit.Sdk.XunitException(
                    $"Server returned JsonRpcError for tools/call. Code={err.Error.Code}, Message='{err.Error.Message}'"));
        }

        transport.OnMessageSent += OnReply;

        try
        {
            await transport.SendMessageAsync(new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Id = reqId,
                Params = JsonSerializer.SerializeToNode(
                    new CallToolRequestParams { Name = toolName },
                    McpJsonUtilities.DefaultOptions)
            }, externalCancellationToken);

            // This completes for either success (JsonRpcResponse) or error (JsonRpcError).
            var obj = await tcs.Task.WaitAsync(externalCancellationToken);

            var response = (JsonRpcResponse)obj;

            // Deserialize a successful response into CallToolResult
            return JsonSerializer.Deserialize<CallToolResult>(
                response.Result, McpJsonUtilities.DefaultOptions)!;
        }
        finally
        {
            transport.OnMessageSent -= OnReply;

            // Deterministic shutdown
            linkedCts.Cancel();
            await transport.DisposeAsync();
            await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task CallTool_ShouldSucceed_WhenFinishesWithinToolTimeout()
    {
        // Arrange: 50ms work, 200ms tool timeout → should succeed.
        var tool = new SlowTool(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200));
        var options = CreateOptions();
        options.ToolCollection ??= [];
        options.ToolCollection.Add(tool);

        // Act
        var result = await ExecuteCallToolRequest(options, tool.Name, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsError, "Tool call should succeed when it finishes within the timeout.");
        var contentText = result.Content.OfType<TextContentBlock>().Single().Text;
        Assert.Contains("Done after 50ms", contentText);
    }

    [Fact]
    public async Task CallTool_ShouldReturnError_WhenToolTimeoutIsExceeded()
    {
        // Arrange: 300ms work, 200ms tool timeout → must time out.
        var tool = new SlowTool(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(200));
        var options = CreateOptions();
        options.ToolCollection ??= [];
        options.ToolCollection.Add(tool);

        // Act
        var result = await ExecuteCallToolRequest(options, tool.Name, TestContext.Current.CancellationToken);

        // Assert (functional)
        Assert.True(result.IsError, "Tool call should fail with IsError=true due to timeout.");

        // Assert (structural): Meta.IsTimeout must be true
        Assert.NotNull(result.Meta);
        Assert.True(
            result.Meta.TryGetPropertyValue("IsTimeout", out var isTimeoutNode),
            "Meta must contain 'IsTimeout' property.");
        Assert.NotNull(isTimeoutNode);
        Assert.True(isTimeoutNode.GetValue<bool>(), "'IsTimeout' must be true.");
    }

    [Fact]
    public async Task CallTool_ShouldReturnError_WhenServerDefaultTimeoutIsExceeded()
    {
        // Arrange: no per-tool timeout; server default is 100ms; work is 300ms → must time out.
        var tool = new SlowTool(TimeSpan.FromMilliseconds(300), toolTimeout: null);
        var options = CreateOptions(defaultTimeout: TimeSpan.FromMilliseconds(100));
        options.ToolCollection ??= [];
        options.ToolCollection.Add(tool);

        // Act
        var result = await ExecuteCallToolRequest(options, tool.Name, TestContext.Current.CancellationToken);

        // Assert (functional)
        Assert.True(result.IsError, "Tool call should fail due to the server's default timeout.");

        // Assert (structural): Meta.IsTimeout must be true
        Assert.NotNull(result.Meta);
        Assert.True(
            result.Meta.TryGetPropertyValue("IsTimeout", out var isTimeoutNode),
            "Meta must contain 'IsTimeout' property.");
        Assert.NotNull(isTimeoutNode);
        Assert.True(isTimeoutNode.GetValue<bool>(), "'IsTimeout' must be true.");
    }

    [Fact]
    public async Task CallTool_ShouldNotRespond_WhenClientCancelsViaJsonRpc()
    {
        // Arrange: no server/tool timeout; user will cancel via $/cancelRequest.
        var tool = new SlowTool(TimeSpan.FromSeconds(10), toolTimeout: null);
        var options = CreateOptions(defaultTimeout: null);
        options.ToolCollection ??= [];
        options.ToolCollection.Add(tool);

        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, options, LoggerFactory);

        using var serverCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runTask = server.RunAsync(serverCts.Token);

        // handshake
        await InitializeServerAsync(transport, options.ProtocolVersion, serverCts.Token);

        var requestId = new RequestId(Guid.NewGuid().ToString("N"));

        var anyReply = new TaskCompletionSource<JsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnAnyReply(JsonRpcMessage m)
        {
            if ((m is JsonRpcResponse r && r.Id.ToString() == requestId.ToString()) ||
                (m is JsonRpcError e && e.Id.ToString() == requestId.ToString()))
            {
                anyReply.TrySetResult(m);
            }
        }

        transport.OnMessageSent += OnAnyReply;

        try
        {
            // 1) send call
            await transport.SendMessageAsync(
                new JsonRpcRequest
                {
                    Method = RequestMethods.ToolsCall,
                    Id = requestId,
                    Params = JsonSerializer.SerializeToNode(
                        new CallToolRequestParams { Name = tool.Name },
                        McpJsonUtilities.DefaultOptions)
                },
                CancellationToken.None);

            await Task.Yield();
            await Task.Delay(200, serverCts.Token);

            // 2) send $/cancelRequest
            var cancelParams = JsonSerializer.SerializeToNode(new { id = requestId.ToString() });
            await transport.SendMessageAsync(
                new JsonRpcNotification
                {
                    Method = NotificationMethods.JsonRpcCancelRequest,
                    Params = cancelParams
                },
                CancellationToken.None);

            // 3) ensure that NO response is emitted for this cancellation
            try
            {
                var _ = await anyReply.Task.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
                throw new Xunit.Sdk.XunitException("Server responded to user-initiated cancellation. Expected: no response.");
            }
            catch (TimeoutException)
            {
                // expected → silent cancel path
            }
        }
        finally
        {
            transport.OnMessageSent -= OnAnyReply;
            serverCts.Cancel();
            await transport.DisposeAsync();
            await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
            await server.DisposeAsync();
        }
    }

}
