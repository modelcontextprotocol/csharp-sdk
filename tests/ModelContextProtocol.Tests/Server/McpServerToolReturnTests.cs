using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using Moq;

namespace ModelContextProtocol.Tests.Server;
public  class McpServerToolReturnTests
{
    [Fact]
    public async Task CanReturnCollectionOfAIContent()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new List<AIContent>() {
                new TextContent("text"),
                new DataContent("data:image/png;base64,1234"),
                new DataContent("data:audio/wav;base64,1234")
            };
        });

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Content.Count);

        Assert.Equal("text", result.Content[0].Text);
        Assert.Equal("text", result.Content[0].Type);

        Assert.Equal("1234", result.Content[1].Data);
        Assert.Equal("image/png", result.Content[1].MimeType);
        Assert.Equal("image", result.Content[1].Type);

        Assert.Equal("1234", result.Content[2].Data);
        Assert.Equal("audio/wav", result.Content[2].MimeType);
        Assert.Equal("audio", result.Content[2].Type);
    }

    [Theory]
    [InlineData("text", "text")]
    [InlineData("data:image/png;base64,1234", "image")]
    [InlineData("data:audio/wav;base64,1234", "audio")]
    public async Task CanReturnSingleAIContent(string data, string type)
    {
        Mock<IMcpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return type switch 
            {
                "text" => (AIContent)new TextContent(data),
                "image" => new DataContent(data),
                "audio" => new DataContent(data),
                _ => throw new ArgumentException("Invalid type")
            };
        });

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Content);
        Assert.Equal(type, result.Content[0].Type);
        
        if (type != "text")
        {
            Assert.NotNull(result.Content[0].MimeType);
            Assert.Equal(data.Split(',').Last(), result.Content[0].Data);
        }
        else
        {
            Assert.Null(result.Content[0].MimeType);
            Assert.Equal(data, result.Content[0].Text);
        }
    }

    [Fact]
    public async Task CanReturnNullAIContent()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return (string?)null;
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.Empty(result.Content);
    }

    [Fact]
    public async Task CanReturnString()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return "42";
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.Single(result.Content);
        Assert.Equal("42", result.Content[0].Text);
        Assert.Equal("text", result.Content[0].Type);
    }

    [Fact]
    public async Task CanReturnCollectionOfStrings()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new List<string>() { "42", "43" };
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Content.Count);
        Assert.Equal("42", result.Content[0].Text);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal("43", result.Content[1].Text);
        Assert.Equal("text", result.Content[1].Type);
    }

    [Fact]
    public async Task CanReturnMcpContent()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new Content { Text = "42", Type = "text" };
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.Single(result.Content);
        Assert.Equal("42", result.Content[0].Text);
        Assert.Equal("text", result.Content[0].Type);
    }

    [Fact]
    public async Task CanReturnCollectionOfMcpContent()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new List<Content>() { new() { Text = "42", Type = "text" }, new() { Data = "1234", Type = "image", MimeType = "image/png" } };
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Content.Count);
        Assert.Equal("42", result.Content[0].Text);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal("1234", result.Content[1].Data);
        Assert.Equal("image", result.Content[1].Type);
        Assert.Equal("image/png", result.Content[1].MimeType);
        Assert.Null(result.Content[1].Text);
    }
}
