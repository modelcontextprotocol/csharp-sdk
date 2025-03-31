using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using Moq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Server;

public class McpServerToolTests
{
    [Fact]
    public void Create_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("function", () => McpServerTool.Create((AIFunction)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((MethodInfo)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((MethodInfo)null!, typeof(object)));
        Assert.Throws<ArgumentNullException>("targetType", () => McpServerTool.Create(typeof(McpServerToolTests).GetMethod(nameof(Create_InvalidArgs_Throws))!, (Type)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((Delegate)null!));

        Assert.NotNull(McpServerTool.Create(typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.InstanceMethod))!, new DisposableToolType()));
        Assert.NotNull(McpServerTool.Create(typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.StaticMethod))!));
        Assert.Throws<ArgumentNullException>("target", () => McpServerTool.Create(typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.InstanceMethod))!, target: null!));
        Assert.Throws<ArgumentException>("target", () => McpServerTool.Create(typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.StaticMethod))!, new DisposableToolType()));
    }

    [Fact]
    public async Task SupportsIMcpServer()
    {
        Mock<IMcpServer> mockServer = new();

        McpServerTool tool = McpServerTool.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return "42";
        });

        Assert.DoesNotContain("server", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", result.Content[0].Text);
    }

    [Fact]
    public async Task SupportsServiceFromDI()
    {
        MyService expectedMyService = new();

        ServiceCollection sc = new();
        sc.AddSingleton(expectedMyService);
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = McpServerTool.Create((MyService actualMyService) =>
        {
            Assert.Same(expectedMyService, actualMyService);
            return "42";
        }, new() { Services = services });

        Assert.DoesNotContain("actualMyService", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));

        Mock<IMcpServer> mockServer = new();

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsError);

        mockServer.SetupGet(x => x.Services).Returns(services);

        result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", result.Content[0].Text);
    }

    [Fact]
    public async Task SupportsOptionalServiceFromDI()
    {
        MyService expectedMyService = new();

        ServiceCollection sc = new();
        sc.AddSingleton(expectedMyService);
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = McpServerTool.Create((MyService? actualMyService = null) =>
        {
            Assert.Null(actualMyService);
            return "42";
        }, new() { Services = services });

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(null!, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", result.Content[0].Text);
    }

    [Fact]
    public async Task SupportsDisposingInstantiatedDisposableTargets()
    {
        McpServerTool tool1 = McpServerTool.Create(
            typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.InstanceMethod))!,
            typeof(DisposableToolType));

        var result = await tool1.InvokeAsync(
            new RequestContext<CallToolRequestParams>(null!, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("""{"disposals":1}""", result.Content[0].Text);
    }

    [Fact]
    public async Task SupportsAsyncDisposingInstantiatedAsyncDisposableTargets()
    {
        McpServerTool tool1 = McpServerTool.Create(
            typeof(AsyncDisposableToolType).GetMethod(nameof(AsyncDisposableToolType.InstanceMethod))!,
            typeof(AsyncDisposableToolType));

        var result = await tool1.InvokeAsync(
            new RequestContext<CallToolRequestParams>(null!, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("""{"asyncDisposals":1}""", result.Content[0].Text);
    }

    [Fact]
    public async Task SupportsAsyncDisposingInstantiatedAsyncDisposableAndDisposableTargets()
    {
        McpServerTool tool1 = McpServerTool.Create(
            typeof(AsyncDisposableAndDisposableToolType).GetMethod(nameof(AsyncDisposableAndDisposableToolType.InstanceMethod))!,
            typeof(AsyncDisposableAndDisposableToolType));

        var result = await tool1.InvokeAsync(
            new RequestContext<CallToolRequestParams>(null!, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("""{"asyncDisposals":1,"disposals":0}""", result.Content[0].Text);
    }


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

    [Fact]
    public async Task CanReturnCallToolResponse()
    {
        CallToolResponse response = new()
        {
            Content = [new() { Text = "text", Type = "text" }, new() { Data = "1234", Type = "image" }]
        };

        Mock<IMcpServer> mockServer = new();
        McpServerTool tool = McpServerTool.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return response;
        });
        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);

        Assert.Same(response, result);

        Assert.Equal(2, result.Content.Count);
        Assert.Equal("text", result.Content[0].Text);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal("1234", result.Content[1].Data);
        Assert.Equal("image", result.Content[1].Type);
    }

    private sealed class MyService;

    private class DisposableToolType : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose()
        {
            Disposals++;
        }

        public object InstanceMethod()
        {
            if (Disposals != 0)
            {
                throw new InvalidOperationException("Dispose was called");
            }

            return this;
        }

        public static object StaticMethod()
        {
            return "42";
        }
    }

    private class AsyncDisposableToolType : IAsyncDisposable
    {
        public int AsyncDisposals { get; private set; }

        public ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            return default;
        }

        public object InstanceMethod()
        {
            if (AsyncDisposals != 0)
            {
                throw new InvalidOperationException("DisposeAsync was called");
            }

            return this;
        }
    }

    private class AsyncDisposableAndDisposableToolType : IAsyncDisposable, IDisposable
    {
        [JsonPropertyOrder(0)]
        public int AsyncDisposals { get; private set; }

        [JsonPropertyOrder(1)]
        public int Disposals { get; private set; }

        public void Dispose()
        {
            Disposals++;
        }

        public ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            return default;
        }

        public object InstanceMethod()
        {
            if (Disposals != 0)
            {
                throw new InvalidOperationException("Dispose was called");
            }

            if (AsyncDisposals != 0)
            {
                throw new InvalidOperationException("DisposeAsync was called");
            }

            return this;
        }
    }
}
