using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Moq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Server;

public partial class McpServerToolTests
{
    [Fact]
    public void Create_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("function", () => McpServerTool.Create((AIFunction)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((MethodInfo)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((MethodInfo)null!, typeof(object)));
        Assert.Throws<ArgumentNullException>("createTargetFunc", () => McpServerTool.Create(typeof(McpServerToolTests).GetMethod(nameof(Create_InvalidArgs_Throws))!, null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((Delegate)null!));

        Assert.NotNull(McpServerTool.Create(typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.InstanceMethod))!, new DisposableToolType()));
        Assert.NotNull(McpServerTool.Create(typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.StaticMethod))!));
        Assert.Throws<ArgumentNullException>("target", () => McpServerTool.Create(typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.InstanceMethod))!, target: null!));
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

        Assert.DoesNotContain("server", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema, McpJsonUtilities.DefaultOptions));

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", result.Content[0].Text);
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task SupportsServiceFromDI(ServiceLifetime injectedArgumentLifetime)
    {
        MyService singletonService = new();

        ServiceCollection sc = new();
        switch (injectedArgumentLifetime)
        {
            case ServiceLifetime.Singleton:
                sc.AddSingleton(singletonService);
                break;

            case ServiceLifetime.Scoped:
                sc.AddScoped(_ => new MyService());
                break;

            case ServiceLifetime.Transient:
                sc.AddTransient(_ => new MyService());
                break;
        }

        sc.AddSingleton(services =>
        {
            return McpServerTool.Create((MyService actualMyService) =>
            {
                Assert.NotNull(actualMyService);
                if (injectedArgumentLifetime == ServiceLifetime.Singleton)
                {
                    Assert.Same(singletonService, actualMyService);
                }

                return "42";
            }, new() { Services = services });
        });

        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = services.GetRequiredService<McpServerTool>();

        Assert.DoesNotContain("actualMyService", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema, McpJsonUtilities.DefaultOptions));

        Mock<IMcpServer> mockServer = new();

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsError);

        result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object) { Services = services },
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
            new RequestContext<CallToolRequestParams>(new Mock<IMcpServer>().Object),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", result.Content[0].Text);
    }

    [Fact]
    public async Task SupportsDisposingInstantiatedDisposableTargets()
    {
        McpServerToolCreateOptions options = new() { SerializerOptions = JsonContext2.Default.Options };
        McpServerTool tool1 = McpServerTool.Create(
            typeof(DisposableToolType).GetMethod(nameof(DisposableToolType.InstanceMethod))!,
            _ => new DisposableToolType(),
            options);

        var result = await tool1.InvokeAsync(
            new RequestContext<CallToolRequestParams>(new Mock<IMcpServer>().Object),
            TestContext.Current.CancellationToken);
        Assert.Equal("""{"disposals":1}""", result.Content[0].Text);
    }

    [Fact]
    public async Task SupportsAsyncDisposingInstantiatedAsyncDisposableTargets()
    {
        McpServerToolCreateOptions options = new() { SerializerOptions = JsonContext2.Default.Options };
        McpServerTool tool1 = McpServerTool.Create(
            typeof(AsyncDisposableToolType).GetMethod(nameof(AsyncDisposableToolType.InstanceMethod))!,
            _ => new AsyncDisposableToolType(),
            options);

        var result = await tool1.InvokeAsync(
            new RequestContext<CallToolRequestParams>(new Mock<IMcpServer>().Object),
            TestContext.Current.CancellationToken);
        Assert.Equal("""{"asyncDisposals":1}""", result.Content[0].Text);
    }

    [Fact]
    public async Task SupportsAsyncDisposingInstantiatedAsyncDisposableAndDisposableTargets()
    {
        ServiceCollection sc = new();
        sc.AddSingleton<MyService>();
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerToolCreateOptions options = new() { SerializerOptions = JsonContext2.Default.Options };
        McpServerTool tool1 = McpServerTool.Create(
            typeof(AsyncDisposableAndDisposableToolType).GetMethod(nameof(AsyncDisposableAndDisposableToolType.InstanceMethod))!,
            static r => ActivatorUtilities.CreateInstance(r.Services!, typeof(AsyncDisposableAndDisposableToolType)),
            options);

        var result = await tool1.InvokeAsync(
            new RequestContext<CallToolRequestParams>(new Mock<IMcpServer>().Object) { Services = services },
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
            new RequestContext<CallToolRequestParams>(mockServer.Object),
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
            new RequestContext<CallToolRequestParams>(mockServer.Object),
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
            new RequestContext<CallToolRequestParams>(mockServer.Object),
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
            new RequestContext<CallToolRequestParams>(mockServer.Object),
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
            new RequestContext<CallToolRequestParams>(mockServer.Object),
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
            new RequestContext<CallToolRequestParams>(mockServer.Object),
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
            new RequestContext<CallToolRequestParams>(mockServer.Object),
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
            new RequestContext<CallToolRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);

        Assert.Same(response, result);

        Assert.Equal(2, result.Content.Count);
        Assert.Equal("text", result.Content[0].Text);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal("1234", result.Content[1].Data);
        Assert.Equal("image", result.Content[1].Type);
    }

    [Fact]
    public async Task SupportsSchemaCreateOptions()
    {
        AIJsonSchemaCreateOptions schemaCreateOptions = new ()
        {
            TransformSchemaNode = (context, node) =>
            {
                node["text"] = "1234";
                return node;
            },
        };

        McpServerTool tool = McpServerTool.Create((int num, string str) =>
        {
            return "42";
        }, new() { SchemaCreateOptions = schemaCreateOptions });

        Assert.All(
            tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject(),
            x => Assert.True(x.Value.TryGetProperty("text", out JsonElement value) && value.ToString() == "1234")
        );
    }

    [Fact]
    public async Task ToolCallError_LogsErrorMessage()
    {
        // Arrange
        var mockLoggerProvider = new MockLoggerProvider();
        var loggerFactory = new LoggerFactory(new[] { mockLoggerProvider });
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        var serviceProvider = services.BuildServiceProvider();

        var toolName = "tool-that-throws";
        var exceptionMessage = "Test exception message";

        McpServerTool tool = McpServerTool.Create(() =>
        {
            throw new InvalidOperationException(exceptionMessage);
        }, new() { Name = toolName, Services = serviceProvider });

        var mockServer = new Mock<IMcpServer>();
        var request = new RequestContext<CallToolRequestParams>(mockServer.Object)
        {
            Params = new CallToolRequestParams() { Name = toolName },
            Services = serviceProvider
        };

        // Act
        var result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal($"An error occurred invoking '{toolName}'.", result.Content[0].Text);

        var errorLog = Assert.Single(mockLoggerProvider.LogMessages, m => m.LogLevel == LogLevel.Error);
        Assert.Equal($"\"{toolName}\" threw an unhandled exception.", errorLog.Message);
        Assert.IsType<InvalidOperationException>(errorLog.Exception);
        Assert.Equal(exceptionMessage, errorLog.Exception.Message);
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
        public AsyncDisposableAndDisposableToolType(MyService service)
        {
            Assert.NotNull(service);
        }

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

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DisposableToolType))]
    [JsonSerializable(typeof(AsyncDisposableToolType))]
    [JsonSerializable(typeof(AsyncDisposableAndDisposableToolType))]
    partial class JsonContext2 : JsonSerializerContext;
}
