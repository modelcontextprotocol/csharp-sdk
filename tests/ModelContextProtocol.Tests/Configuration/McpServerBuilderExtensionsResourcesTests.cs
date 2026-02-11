using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Collections;
using System.ComponentModel;
using System.Threading.Channels;
using static ModelContextProtocol.Tests.Configuration.McpServerBuilderExtensionsPromptsTests;

namespace ModelContextProtocol.Tests.Configuration;

public partial class McpServerBuilderExtensionsResourcesTests : ClientServerTestBase
{
    public McpServerBuilderExtensionsResourcesTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder
                .WithListResourcesHandler(async (request, cancellationToken) =>
                    {
                        var cursor = request.Params?.Cursor;
                        switch (cursor)
                        {
                            case null:
                                return new()
                                {
                                    NextCursor = "abc",
                                    Resources = [new()
                                    {
                                        Name = "Resource1",
                                        Uri = "test://resource1",
                                    }],
                                };

                            case "abc":
                                return new()
                                {
                                    NextCursor = "def",
                                    Resources = [new()
                                    {
                                        Name = "Resource2",
                                        Uri = "test://resource2",
                                    }],
                                };

                            case "def":
                                return new()
                                {
                                    NextCursor = null,
                                    Resources = [new()
                                    {
                                        Name = "Resource3",
                                        Uri = "test://resource3",
                                    }],
                                };

                            default:
                                throw new McpProtocolException($"Unexpected cursor: '{cursor}'", McpErrorCode.InvalidParams);
                        }
                    })
                .WithListResourceTemplatesHandler(async (request, cancellationToken) =>
                    {
                        var cursor = request.Params?.Cursor;
                        switch (cursor)
                        {
                            case null:
                                return new()
                                {
                                    NextCursor = "abc",
                                    ResourceTemplates = [new()
                                    {
                                        Name = "ResourceTemplate1",
                                        UriTemplate = "test://resourceTemplate/{id}",
                                    }],
                                };
                            case "abc":
                                return new()
                                {
                                    NextCursor = null,
                                    ResourceTemplates = [new()
                                    {
                                        Name = "ResourceTemplate2",
                                        UriTemplate = "test://resourceTemplate2/{id}",
                                    }],
                                };
                            default:
                                throw new McpProtocolException($"Unexpected cursor: '{cursor}'", McpErrorCode.InvalidParams);
                        }
                    })
        .WithReadResourceHandler(async (request, cancellationToken) =>
        {
            switch (request.Params?.Uri)
            {
                case "test://Resource1":
                case "test://Resource2":
                case "test://Resource3":
                case "test://ResourceTemplate1":
                case "test://ResourceTemplate2":
                    return new ReadResourceResult
                    {
                        Contents = [new TextResourceContents { Text = request.Params?.Uri ?? "(null)", Uri = request.Params?.Uri ?? "(null)" }]
                    };
            }

            throw new McpProtocolException($"Resource not found: {request.Params?.Uri}", McpErrorCode.ResourceNotFound);
        })
        .WithResources<SimpleResources>();
    }

    [Fact]
    public void Adds_Resources_To_Server()
    {
        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var resources = serverOptions.ResourceCollection;
        Assert.NotNull(resources);
        Assert.NotEmpty(resources);
    }

    [Fact]
    public async Task Can_List_And_Call_Registered_Resources()
    {
        await using McpClient client = await CreateMcpClientForServer();

        Assert.NotNull(client.ServerCapabilities.Resources);

        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(7, resources.Count);

        var resource = resources.First(t => t.Name == "some_neat_direct_resource");
        Assert.Equal("Some neat direct resource", resource.Description);

        var result = await resource.ReadAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Contents);
        Assert.Equal("This is a neat resource", Assert.IsType<TextResourceContents>(result.Contents[0]).Text);
    }

    [Fact]
    public async Task Can_List_And_Call_Registered_ResourceTemplates()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var resources = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, resources.Count);

        var resource = resources.First(t => t.Name == "some_neat_templated_resource");
        Assert.Equal("Some neat resource with parameters", resource.Description);

        var result = await resource.ReadAsync(new Dictionary<string, object?>() { ["name"] = "hello" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Contents);
        Assert.Equal("This is a neat resource with parameters: hello", Assert.IsType<TextResourceContents>(result.Contents[0]).Text);
    }

    [Fact]
    public async Task Can_Be_Notified_Of_Resource_Changes()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(7, resources.Count);

        Channel<JsonRpcNotification> listChanged = Channel.CreateUnbounded<JsonRpcNotification>();
        var notificationRead = listChanged.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.False(notificationRead.IsCompleted);

        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var serverResources = serverOptions.ResourceCollection;
        Assert.NotNull(serverResources);

        var newResource = McpServerResource.Create([McpServerResource(Name = "NewResource")] () => "42");
        await using (client.RegisterNotificationHandler("notifications/resources/list_changed", (notification, cancellationToken) =>
            {
                listChanged.Writer.TryWrite(notification);
                return default;
            }))
        {
            serverResources.Add(newResource);
            await notificationRead;

            resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(8, resources.Count);
            Assert.Contains(resources, t => t.Name == "NewResource");

            notificationRead = listChanged.Reader.ReadAsync(TestContext.Current.CancellationToken);
            Assert.False(notificationRead.IsCompleted);
            serverResources.Remove(newResource);
            await notificationRead;
        }

        resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(7, resources.Count);
        Assert.DoesNotContain(resources, t => t.Name == "NewResource");
    }

    [Fact]
    public async Task AttributeProperties_Propagated()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(resources);
        Assert.NotEmpty(resources);
        McpClientResource resource = resources.First(t => t.Name == "some_neat_direct_resource");
        Assert.Equal("This is a title", resource.Title);

        Assert.NotNull(resource.ProtocolResource.Icons);
        Assert.NotEmpty(resource.ProtocolResource.Icons);
        var resourceIcon = Assert.Single(resource.ProtocolResource.Icons);
        Assert.Equal("https://example.com/direct-resource-icon.svg", resourceIcon.Source);
        Assert.Null(resourceIcon.Theme);

        var resourceTemplates = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(resourceTemplates);
        Assert.NotEmpty(resourceTemplates);
        McpClientResourceTemplate resourceTemplate = resourceTemplates.First(t => t.Name == "some_neat_templated_resource");
        Assert.Equal("This is another title", resourceTemplate.Title);

        Assert.NotNull(resourceTemplate.ProtocolResourceTemplate.Icons);
        Assert.NotEmpty(resourceTemplate.ProtocolResourceTemplate.Icons);
        var templateIcon = Assert.Single(resourceTemplate.ProtocolResourceTemplate.Icons);
        Assert.Equal("https://example.com/templated-resource-icon.svg", templateIcon.Source);
        Assert.Null(templateIcon.Theme);
    }

    [Fact]
    public async Task Throws_When_Resource_Fails()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ReadResourceAsync(
            $"resource://mcp/{nameof(SimpleResources.ThrowsException)}",
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Logs_Resource_Uri_On_Successful_Read()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.ReadResourceAsync(
            "resource://mcp/some_neat_direct_resource",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        var infoLog = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "ReadResource \"resource://mcp/some_neat_direct_resource\" completed.");
        Assert.Equal(LogLevel.Information, infoLog.LogLevel);
    }

    [Fact]
    public async Task Logs_Resource_Uri_When_Resource_Throws()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ReadResourceAsync(
            "resource://mcp/throws_exception",
            cancellationToken: TestContext.Current.CancellationToken));

        var errorLog = Assert.Single(MockLoggerProvider.LogMessages, m => m.LogLevel == LogLevel.Error);
        Assert.Equal("ReadResource \"resource://mcp/throws_exception\" threw an unhandled exception.", errorLog.Message);
        Assert.IsType<InvalidOperationException>(errorLog.Exception);
    }

    [Fact]
    public async Task Logs_Resource_Error_When_Resource_Throws_OperationCanceledException()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ReadResourceAsync(
            "resource://mcp/throws_operation_canceled_exception",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Error &&
            m.Message == "ReadResource \"resource://mcp/throws_operation_canceled_exception\" threw an unhandled exception." &&
            m.Exception is OperationCanceledException);

        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning &&
            m.Message.Contains("request handler failed"));
    }

    [Fact]
    public async Task Logs_Resource_Error_When_Resource_Throws_McpProtocolException()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ReadResourceAsync(
            "resource://mcp/throws_mcp_protocol_exception",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Error &&
            m.Message == "ReadResource \"resource://mcp/throws_mcp_protocol_exception\" threw an unhandled exception." &&
            m.Exception is McpProtocolException);

        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning &&
            m.Message.Contains("request handler failed"));
    }

    [Fact]
    public async Task Throws_Exception_On_Unknown_Resource()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var e = await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ReadResourceAsync(
            "test:///NotRegisteredResource",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Resource not found", e.Message);
        Assert.Equal(McpErrorCode.ResourceNotFound, e.ErrorCode);
    }

    [Fact]
    public void WithResources_InvalidArgs_Throws()
    {
        IMcpServerBuilder builder = new ServiceCollection().AddMcpServer();

        Assert.Throws<ArgumentNullException>("resourceTemplates", () => builder.WithResources((IEnumerable<McpServerResource>)null!));
        Assert.Throws<ArgumentNullException>("resourceTemplateTypes", () => builder.WithResources((IEnumerable<Type>)null!));
        Assert.Throws<ArgumentNullException>("target", () => builder.WithResources<object>(target: null!));

        IMcpServerBuilder nullBuilder = null!;
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithResources<object>());
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithResources(new object()));
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithResources(Array.Empty<Type>()));
        Assert.Throws<ArgumentNullException>("builder", () => nullBuilder.WithResourcesFromAssembly());
    }

    [Fact]
    public async Task WithResources_TargetInstance_UsesTarget()
    {
        ServiceCollection sc = new();

        var target = new ResourceWithId(new ObjectWithId() { Id = "42" });
        sc.AddMcpServer().WithResources(target);

        McpServerResource resource = sc.BuildServiceProvider().GetServices<McpServerResource>().First(t => t.ProtocolResource?.Name == "returns_string");
        var result = await resource.ReadAsync(new RequestContext<ReadResourceRequestParams>(new Mock<McpServer>().Object, new JsonRpcRequest { Method = "test", Id = new RequestId("1") })
        {
            Params = new()
            {
                Uri = "returns://string"
            }
        }, TestContext.Current.CancellationToken);

        Assert.Equal(target.ReturnsString(), (result?.Contents[0] as TextResourceContents)?.Text);
    }

    [Fact]
    public async Task WithResources_TargetInstance_UsesEnumerableImplementation()
    {
        ServiceCollection sc = new();

        sc.AddMcpServer().WithResources(new MyResourceProvider());

        var resources = sc.BuildServiceProvider().GetServices<McpServerResource>().ToArray();
        Assert.Equal(2, resources.Length);
        Assert.Contains(resources, t => t.ProtocolResource?.Name == "Returns42");
        Assert.Contains(resources, t => t.ProtocolResource?.Name == "Returns43");
    }

    private sealed class MyResourceProvider : IEnumerable<McpServerResource>
    {
        public IEnumerator<McpServerResource> GetEnumerator()
        {
            yield return McpServerResource.Create(() => "42", new() { Name = "Returns42" });
            yield return McpServerResource.Create(() => "43", new() { Name = "Returns43" });
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Empty_Enumerables_Is_Allowed()
    {
        IMcpServerBuilder builder = new ServiceCollection().AddMcpServer();

        builder.WithResources(resourceTemplates: Array.Empty<McpServerResource>()); // no exception
        builder.WithResources(resourceTemplateTypes: Array.Empty<Type>()); // no exception
        builder.WithResources<object>(); // no exception even though no resources exposed
        builder.WithResourcesFromAssembly(typeof(AIFunction).Assembly); // no exception even though no resources exposed
    }

    [Fact]
    public void Register_Resources_From_Current_Assembly()
    {
        ServiceCollection sc = new();
        sc.AddMcpServer().WithResourcesFromAssembly();
        IServiceProvider services = sc.BuildServiceProvider();

        Assert.Contains(services.GetServices<McpServerResource>(), t => t.ProtocolResource?.Uri == $"resource://mcp/some_neat_direct_resource");
        Assert.Contains(services.GetServices<McpServerResource>(), t => t.ProtocolResourceTemplate?.UriTemplate == $"resource://mcp/some_neat_templated_resource{{?name}}");
    }

    [Fact]
    public void Register_Resources_From_Multiple_Sources()
    {
        ServiceCollection sc = new();
        sc.AddMcpServer()
            .WithResources<SimpleResources>()
            .WithResources<MoreResources>()
            .WithResources([McpServerResource.Create(() => "42", new() { UriTemplate = "myResources:///returns42/{something}" })]);
        IServiceProvider services = sc.BuildServiceProvider();

        Assert.Contains(services.GetServices<McpServerResource>(), t => t.ProtocolResource?.Uri == $"resource://mcp/some_neat_direct_resource");
        Assert.Contains(services.GetServices<McpServerResource>(), t => t.ProtocolResourceTemplate?.UriTemplate == $"resource://mcp/some_neat_templated_resource{{?name}}");
        Assert.Contains(services.GetServices<McpServerResource>(), t => t.ProtocolResourceTemplate?.UriTemplate == $"resource://mcp/another_neat_direct_resource");
        Assert.Contains(services.GetServices<McpServerResource>(), t => t.ProtocolResourceTemplate.UriTemplate == "myResources:///returns42/{something}");
    }

    [McpServerResourceType]
    public sealed class SimpleResources
    {
        [McpServerResource(Title = "This is a title", IconSource = "https://example.com/direct-resource-icon.svg"), Description("Some neat direct resource")]
        public static string SomeNeatDirectResource() => "This is a neat resource";

        [McpServerResource(Title = "This is another title", IconSource = "https://example.com/templated-resource-icon.svg"), Description("Some neat resource with parameters")]
        public static string SomeNeatTemplatedResource(string name) => $"This is a neat resource with parameters: {name}";

        [McpServerResource]
        public static string ThrowsException() => throw new InvalidOperationException("uh oh");

        [McpServerResource]
        public static string ThrowsOperationCanceledException() => throw new OperationCanceledException("Resource was canceled");

        [McpServerResource]
        public static string ThrowsMcpProtocolException() => throw new McpProtocolException("Resource protocol error", McpErrorCode.InvalidParams);
    }

    [McpServerResourceType]
    public sealed class MoreResources
    {
        [McpServerResource, Description("Another neat direct resource")]
        public static string AnotherNeatDirectResource() => "This is a neat resource";
    }

    [McpServerResourceType]
    public sealed class ResourceWithId(ObjectWithId id)
    {
        [McpServerResource(UriTemplate = "returns://string")]
        public string ReturnsString() => $"Id: {id.Id}";
    }
}
