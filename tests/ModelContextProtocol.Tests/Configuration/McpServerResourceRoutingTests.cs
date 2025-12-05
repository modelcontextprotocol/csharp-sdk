using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Configuration;

public sealed class McpServerResourceRoutingTests(ITestOutputHelper testOutputHelper) : ClientServerTestBase(testOutputHelper)
{
    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithResources([
            McpServerResource.Create(options: new() { UriTemplate = "test://resource/non-templated" } , method: () => "static"),
            McpServerResource.Create(options: new() { UriTemplate = "test://resource/{id}" }, method: (string id) => $"template: {id}"),
            McpServerResource.Create(options: new() { UriTemplate = "test://params{?a1,a2,a3}" }, method: (string a1, string a2, string a3) => $"params: {a1}, {a2}, {a3}"),
        ]);
    }

    [Fact]
    public async Task MultipleTemplatedResources_MatchesCorrectResource()
    {
        // Verify that when multiple templated resources exist, the correct one is matched based on the URI pattern, not just the first one.
        // Regression test for https://github.com/modelcontextprotocol/csharp-sdk/issues/821.
        await using McpClient client = await CreateMcpClientForServer();

        var nonTemplatedResult = await client.ReadResourceAsync("test://resource/non-templated", null, TestContext.Current.CancellationToken);
        Assert.Equal("static", ((TextResourceContents)nonTemplatedResult.Contents[0]).Text);

        var templatedResult = await client.ReadResourceAsync("test://resource/12345", null, TestContext.Current.CancellationToken);
        Assert.Equal("template: 12345", ((TextResourceContents)templatedResult.Contents[0]).Text);

        var exactTemplatedResult = await client.ReadResourceAsync("test://resource/{id}", null, TestContext.Current.CancellationToken);
        Assert.Equal("template: {id}", ((TextResourceContents)exactTemplatedResult.Contents[0]).Text);

        var paramsResult = await client.ReadResourceAsync("test://params?a1=a&a2=b&a3=c", null, TestContext.Current.CancellationToken);
        Assert.Equal("params: a, b, c", ((TextResourceContents)paramsResult.Contents[0]).Text);

        var mcpEx = await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ReadResourceAsync("test://params{?a1,a2,a3}", null, TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.InvalidParams, mcpEx.ErrorCode);
        Assert.Equal("Request failed (remote): Unknown resource URI: 'test://params{?a1,a2,a3}'", mcpEx.Message);
    }
}
