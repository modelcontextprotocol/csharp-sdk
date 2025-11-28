using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

public class McpClientMetaTests : ClientServerTestBase
{
    public McpClientMetaTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

   protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(o =>
        {
            o.ServerInfo = new Implementation
            {
                Name = "test-server",
                Version = "1.0.0",
                Description = "A test server for unit testing",
                WebsiteUrl = "https://example.com",
            };
            o.ToolCollection = new ();
        });
    }

    [Fact]
    public async Task ToolCallWithMetaFields()
    {
        Server.ServerOptions.ToolCollection?.Add(McpServerTool.Create(
            async (RequestContext<CallToolRequestParams> context) =>
            {
                // Access the foo property of _meta field from the request parameters
                var metaFoo = context.Params?.Meta?["foo"]?.ToString();

                // Assert that the meta foo is correctly passed
                Assert.NotNull(metaFoo);

                return $"Meta foo is {metaFoo}";
            },
            new () { Name = "echo_meta" }));

        await using McpClient client = await CreateMcpClientForServer();

        var requestOptions = new RequestOptions()
        {
            Meta = new JsonObject()
            {
                { "foo", "barbaz" }
            }
        };

        var result = await client.CallToolAsync("echo_meta", options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);
        // Assert.Contains("barbaz", result?.ToString());
        Assert.NotNull(result);
        Assert.Null(result.IsError);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textContent);
        Assert.Contains("barbaz", textContent.Text);
    }
}