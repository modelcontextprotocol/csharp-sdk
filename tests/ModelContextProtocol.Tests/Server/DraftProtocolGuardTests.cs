using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public sealed class DraftProtocolGuardTests : ClientServerTestBase
{
    public DraftProtocolGuardTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ProtocolVersion = "DRAFT-2026-v1";
        });

        mcpServerBuilder.WithTools([
            McpServerTool.Create(AssertElicitAsyncGuardAsync, new() { Name = "assert-elicit-guard" }),
            McpServerTool.Create(AssertSampleAsyncGuardAsync, new() { Name = "assert-sample-guard" }),
            McpServerTool.Create(AssertRequestRootsAsyncGuardAsync, new() { Name = "assert-roots-guard" }),
        ]);
    }

    [Fact]
    public async Task ElicitAsync_ThrowsUnderDraftProtocol()
    {
        StartServer();
        await using var client = await CreateDraftClientAsync();

        var result = await client.CallToolAsync("assert-elicit-guard", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task SampleAsync_ThrowsUnderDraftProtocol()
    {
        StartServer();
        await using var client = await CreateDraftClientAsync();

        var result = await client.CallToolAsync("assert-sample-guard", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task RequestRootsAsync_ThrowsUnderDraftProtocol()
    {
        StartServer();
        await using var client = await CreateDraftClientAsync();

        var result = await client.CallToolAsync("assert-roots-guard", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    private Task<McpClient> CreateDraftClientAsync() =>
        CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = "DRAFT-2026-v1" });

    private static async Task<string> AssertElicitAsyncGuardAsync(McpServer server, CancellationToken cancellationToken)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.ElicitAsync(new ElicitRequestParams
            {
                Message = "Need input",
                RequestedSchema = new(),
            }, cancellationToken).AsTask());

        Assert.Contains("DRAFT-2026-v1", exception.Message);
        Assert.Contains("InputRequest.ForElicitation", exception.Message);
        return "ok";
    }

    private static async Task<string> AssertSampleAsyncGuardAsync(McpServer server, CancellationToken cancellationToken)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.SampleAsync(new CreateMessageRequestParams
            {
                Messages =
                [
                    new SamplingMessage
                    {
                        Role = Role.User,
                        Content = [new TextContentBlock { Text = "Hello" }],
                    },
                ],
                MaxTokens = 1,
            }, cancellationToken).AsTask());

        Assert.Contains("DRAFT-2026-v1", exception.Message);
        Assert.Contains("InputRequest.ForSampling", exception.Message);
        return "ok";
    }

    private static async Task<string> AssertRequestRootsAsyncGuardAsync(McpServer server, CancellationToken cancellationToken)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken).AsTask());

        Assert.Contains("DRAFT-2026-v1", exception.Message);
        Assert.Contains("InputRequest.ForRootsList", exception.Message);
        return "ok";
    }
}
