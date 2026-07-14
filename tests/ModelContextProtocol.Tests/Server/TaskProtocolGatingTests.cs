using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

#pragma warning disable MCPEXP001

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that the SEP-2663 Tasks extension is gated to the 2026-07-28 protocol revision on both the
/// client and the server. Explicit task operations throw on a legacy session; best-effort task
/// augmentation silently downgrades to a direct result so that legacy peers never see a task.
/// </summary>
public class TaskProtocolGatingTests : ClientServerTestBase
{
    private const string LatestStableVersion = "2025-11-25";

    private const string ClientCapabilitiesMetaKey = "io.modelcontextprotocol/clientCapabilities";
    private const string ExtensionsKey = "extensions";

    public TaskProtocolGatingTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = new InMemoryMcpTaskStore
            {
                DefaultPollIntervalMs = 50,
            };
        });

        mcpServerBuilder.WithTools([McpServerTool.Create(
            async (string input, CancellationToken ct) =>
            {
                await Task.Delay(50, ct);
                return $"Processed: {input}";
            },
            new McpServerToolCreateOptions
            {
                Name = "test-tool",
                Description = "A test tool"
            })]);
    }

    private static IDictionary<string, JsonElement> CreateArguments(string key, string value)
    {
        return new Dictionary<string, JsonElement>
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone()
        };
    }

    private static JsonObject CreateForgedTaskOptInMeta() =>
        new()
        {
            [ClientCapabilitiesMetaKey] = new JsonObject
            {
                [ExtensionsKey] = new JsonObject
                {
                    [McpExtensions.Tasks] = new JsonObject(),
                },
            },
        };

    [Fact]
    public async Task LegacyClient_GetTaskAsync_ThrowsInvalidOperationException()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.GetTaskAsync("some-task-id", ct));

        Assert.Contains("newer protocol revision that supports tasks", ex.Message);
    }

    [Fact]
    public async Task LegacyClient_UpdateTaskAsync_ThrowsInvalidOperationException()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.UpdateTaskAsync(new UpdateTaskRequestParams { TaskId = "some-task-id" }, ct));

        Assert.Contains("newer protocol revision that supports tasks", ex.Message);
    }

    [Fact]
    public async Task LegacyClient_CancelTaskAsync_ThrowsInvalidOperationException()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.CancelTaskAsync("some-task-id", ct));

        Assert.Contains("newer protocol revision that supports tasks", ex.Message);
    }

    [Fact]
    public async Task LegacyClient_CallToolRaw_ReturnsDirectResult_NoTaskCreated()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });
        var ct = TestContext.Current.CancellationToken;

        var result = await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "legacy"),
            }, ct);

        Assert.False(result.IsTask);
        Assert.NotNull(result.Result);
    }

    [Fact]
    public async Task LegacyClient_CallToolRaw_WithForgedTaskOptIn_RejectsReservedMetadata()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });
        var ct = TestContext.Current.CancellationToken;

        // Forge a SEP-2575 capabilities envelope carrying the tasks extension opt-in on a legacy
        // request. The server rejects reserved per-request metadata before it can affect behavior.
        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () => await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "forged"),
                Meta = CreateForgedTaskOptInMeta(),
            }, ct));

        Assert.Equal(McpErrorCode.InvalidRequest, ex.ErrorCode);
        Assert.Contains(ClientCapabilitiesMetaKey, ex.Message);
    }

    [Fact]
    public async Task LegacyClient_RawTasksGetRequest_ReturnsMethodNotFound()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = LatestStableVersion });
        var ct = TestContext.Current.CancellationToken;

        // Bypass the typed GetTaskAsync client guard by sending a raw tasks/get request. The server
        // gates tasks/* to the 2026-07-28 protocol and must reject this legacy request with MethodNotFound.
        var request = new JsonRpcRequest
        {
            Method = RequestMethods.TasksGet,
            Params = JsonSerializer.SerializeToNode(
                new GetTaskRequestParams { TaskId = "some-task-id" },
                McpJsonUtilities.DefaultOptions),
        };

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.SendRequestAsync(request, ct));

        Assert.Equal(McpErrorCode.MethodNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task July2026ProtocolClient_CallToolRaw_CreatesTask()
    {
        // Sanity: the default client negotiates the 2026-07-28 protocol, so the task flow still works.
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var result = await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "july2026"),
            }, ct);

        Assert.True(result.IsTask);
        Assert.NotNull(result.TaskCreated);
    }
}
