using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using Moq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Server;

public partial class McpServerResourceTests
{
    [Fact]
    public void CanCreateServerWithResources()
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithListResourcesHandler(async (ctx, ct) =>
            {
                return new ListResourcesResult
                {
                    Resources =
                    [
                        new Resource { Name = "Static Resource", Description = "A static resource with a numeric ID", Uri = "test://static/resource/foo.txt" }
                    ]
                };
            })
            .WithReadResourceHandler(async (ctx, ct) =>
            {
                return new ReadResourceResult
                {
                    Contents = [new TextResourceContents
                    {
                        Uri = ctx.Params!.Uri!,
                        Text = "Static Resource",
                        MimeType = "text/plain",
                    }]
                };
            });

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMcpServer>();
    }

    [Fact]
    public void CreatingReadHandlerWithNoListHandlerSucceeds()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithReadResourceHandler(async (ctx, ct) =>
            {
                return new ReadResourceResult
                {
                    Contents = [new TextResourceContents
                    {
                        Uri = ctx.Params!.Uri!,
                        Text = "Static Resource",
                        MimeType = "text/plain",
                    }]
                };
            });
        var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IMcpServer>();
    }

    [Fact]
    public void Create_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("function", () => McpServerResource.Create((AIFunction)null!, new() { Uri = "test://hello" }));
        Assert.Throws<ArgumentNullException>("method", () => McpServerResource.Create((MethodInfo)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerResource.Create((MethodInfo)null!, typeof(object)));
        Assert.Throws<ArgumentNullException>("targetType", () => McpServerResource.Create(typeof(McpServerResourceTests).GetMethod(nameof(Create_InvalidArgs_Throws))!, (Type)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerResource.Create((Delegate)null!));

        Assert.NotNull(McpServerResource.Create(typeof(DisposableResourceType).GetMethod(nameof(DisposableResourceType.InstanceMethod))!, new DisposableResourceType()));
        Assert.NotNull(McpServerResource.Create(typeof(DisposableResourceType).GetMethod(nameof(DisposableResourceType.StaticMethod))!));
        Assert.Throws<ArgumentNullException>("target", () => McpServerResource.Create(typeof(DisposableResourceType).GetMethod(nameof(DisposableResourceType.InstanceMethod))!, target: null!));
    }

    [Fact]
    public async Task SupportsResourceAsProperty()
    {
        Mock<IMcpServer> mockServer = new();

        McpServerResource resource = McpServerResource.Create(typeof(MyResource).GetProperty("Something")!);

        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);
    }

    private sealed class MyResource
    {
        [McpServerResource(Uri = "test://someProp")]
        public static string Something => "42";
    }

    [Fact]
    public async Task SupportsIMcpServer()
    {
        Mock<IMcpServer> mockServer = new();

        McpServerResource resource = McpServerResource.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return "42";
        });

        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);
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
            return McpServerResource.Create((MyService actualMyService) =>
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

        McpServerResource resource = services.GetRequiredService<McpServerResource>();

        Mock<IMcpServer> mockServer = new();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken));

        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object) { Services = services },
            TestContext.Current.CancellationToken);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task SupportsOptionalServiceFromDI()
    {
        MyService expectedMyService = new();

        ServiceCollection sc = new();
        sc.AddSingleton(expectedMyService);
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerResource resource = McpServerResource.Create((MyService? actualMyService = null) =>
        {
            Assert.Null(actualMyService);
            return "42";
        }, new() { Services = services });

        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<IMcpServer>().Object),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task SupportsDisposingInstantiatedDisposableTargets()
    {
        int before = DisposableResourceType.Disposals;

        McpServerResource resource1 = McpServerResource.Create(
            typeof(DisposableResourceType).GetMethod(nameof(DisposableResourceType.InstanceMethod))!,
            typeof(DisposableResourceType));

        var result = await resource1.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<IMcpServer>().Object),
            TestContext.Current.CancellationToken);
        Assert.Equal("0", ((TextResourceContents)result.Contents[0]).Text);

        Assert.Equal(1, DisposableResourceType.Disposals);
    }

    [Fact]
    public async Task CanReturnReadResult()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new ReadResourceResult() { Contents = new List<ResourceContents>() { new TextResourceContents() { Text = "hello" } } };
        });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.Single(result.Contents);
        Assert.Equal("hello", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task CanReturnResourceContents()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new TextResourceContents() { Text = "hello" };
        });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.Single(result.Contents);
        Assert.Equal("hello", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task CanReturnCollectionOfResourceContents()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new List<ResourceContents>()
            {
                new TextResourceContents() { Text = "hello" },
                new BlobResourceContents() { Blob = Convert.ToBase64String(new byte[] { 1, 2, 3 }) },
            };
        });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Contents.Count);
        Assert.Equal("hello", ((TextResourceContents)result.Contents[0]).Text);
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), ((BlobResourceContents)result.Contents[1]).Blob);
    }

    [Fact]
    public async Task CanReturnString()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return "42";
        });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.Single(result.Contents);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task CanReturnCollectionOfStrings()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new List<string>() { "42", "43" };
        });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Contents.Count);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);
        Assert.Equal("43", ((TextResourceContents)result.Contents[1]).Text);
    }

    [Fact]
    public async Task CanReturnDataContent()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new DataContent(new byte[] { 0, 1, 2 }, "application/octet-stream");
        });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.Single(result.Contents);
        Assert.Equal(Convert.ToBase64String(new byte[] { 0, 1, 2 }), ((BlobResourceContents)result.Contents[0]).Blob);
        Assert.Equal("application/octet-stream", ((BlobResourceContents)result.Contents[0]).MimeType);
    }

    [Fact]
    public async Task CanReturnCollectionOfDataContent()
    {
        Mock<IMcpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new List<DataContent>()
            {
                new DataContent(new byte[] { 0, 1, 2 }, "application/octet-stream"),
                new DataContent(new byte[] { 4, 5, 6 }, "application/json"),
            };
        });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Contents.Count);
        Assert.Equal(Convert.ToBase64String(new byte[] { 0, 1, 2 }), ((BlobResourceContents)result.Contents[0]).Blob);
        Assert.Equal("application/octet-stream", ((BlobResourceContents)result.Contents[0]).MimeType);
        Assert.Equal(Convert.ToBase64String(new byte[] { 4, 5, 6 }), ((BlobResourceContents)result.Contents[1]).Blob);
        Assert.Equal("application/json", ((BlobResourceContents)result.Contents[1]).MimeType);
    }

    private sealed class MyService;

    private class DisposableResourceType : IDisposable
    {
        public static int Disposals { get; private set; }

        public void Dispose() => Disposals++;

        [McpServerResource(Uri = "test://static/resource/instanceMethod")]
        public object InstanceMethod() => Disposals.ToString();

        [McpServerResource(Uri = "test://static/resource/staticMethod")]
        public static object StaticMethod() => "42";
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DisposableResourceType))]
    partial class JsonContext5 : JsonSerializerContext;
}
