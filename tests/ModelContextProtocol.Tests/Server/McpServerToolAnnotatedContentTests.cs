using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class McpServerToolAnnotatedContentTests : ClientServerTestBase
{
    public McpServerToolAnnotatedContentTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools<AnnotatedContentTools>();
    }

    [Fact]
    public async Task CallTool_TextBlockWithAnnotations_RoundTrips()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync("annotated_text", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("hello", block.Text);
        Assert.NotNull(block.Annotations);
        Assert.Equal([Role.User], block.Annotations.Audience);
        Assert.Equal((float?)0.5, block.Annotations.Priority);
    }

    [Fact]
    public async Task CallTool_ResourceLinkWithAnnotationsAndIcons_RoundTrips()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync("annotated_link", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var block = Assert.IsType<ResourceLinkBlock>(Assert.Single(result.Content));
        Assert.Equal("file:///test", block.Uri);
        Assert.NotNull(block.Annotations);
        Assert.Equal([Role.Assistant], block.Annotations.Audience);
        Assert.Equal("https://example.com/icon.png", Assert.Single(Assert.IsAssignableFrom<IList<Icon>>(block.Icons)).Source);
    }

    [McpServerToolType]
    public sealed class AnnotatedContentTools
    {
        [McpServerTool(Name = "annotated_text")]
        public static CallToolResult AnnotatedText() => new()
        {
            Content = [new TextContentBlock { Text = "hello", Annotations = new() { Audience = [Role.User], Priority = 0.5f } }],
        };

        [McpServerTool(Name = "annotated_link")]
        public static CallToolResult AnnotatedLink() => new()
        {
            Content =
            [
                new ResourceLinkBlock
                {
                    Uri = "file:///test",
                    Name = "test",
                    Annotations = new() { Audience = [Role.Assistant] },
                    Icons = [new Icon { Source = "https://example.com/icon.png" }],
                },
            ],
        };
    }
}
