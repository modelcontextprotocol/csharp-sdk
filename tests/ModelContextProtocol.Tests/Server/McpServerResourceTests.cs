using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Server;

public partial class McpServerResourceTests
{
    private static JsonRpcRequest CreateTestJsonRpcRequest()
    {
        return new JsonRpcRequest
        {
            Id = new RequestId("test-id"),
            Method = "test/method",
            Params = null
        };
    }

    public McpServerResourceTests()
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    [Fact]
    public void CanCreateServerWithResource()
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
                        new Resource { Name = "Static Resource", Description = "A static resource with a numeric ID", Uri = "test://static/resource" }
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

        provider.GetRequiredService<McpServer>();
    }


    [Fact]
    public void CanCreateServerWithResourceTemplates()
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithListResourceTemplatesHandler(async (ctx, ct) =>
            {
                return new ListResourceTemplatesResult
                {
                    ResourceTemplates =
                    [
                        new ResourceTemplate { Name = "Static Resource", Description = "A static resource with a numeric ID", UriTemplate = "test://static/resource/{id}" }
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

        provider.GetRequiredService<McpServer>();
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

        sp.GetRequiredService<McpServer>();
    }

    [Fact]
    public void Create_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("function", () => McpServerResource.Create((AIFunction)null!, new() { UriTemplate = "test://hello" }));
        Assert.Throws<ArgumentNullException>("method", () => McpServerResource.Create((MethodInfo)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerResource.Create((MethodInfo)null!, _ => new object()));
        Assert.Throws<ArgumentNullException>("createTargetFunc", () => McpServerResource.Create(typeof(McpServerResourceTests).GetMethod(nameof(Create_InvalidArgs_Throws))!, null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerResource.Create((Delegate)null!));

        Assert.NotNull(McpServerResource.Create(typeof(DisposableResourceType).GetMethod(nameof(DisposableResourceType.InstanceMethod))!, new DisposableResourceType()));
        Assert.NotNull(McpServerResource.Create(typeof(DisposableResourceType).GetMethod(nameof(DisposableResourceType.StaticMethod))!));
        Assert.Throws<ArgumentNullException>("target", () => McpServerResource.Create(typeof(DisposableResourceType).GetMethod(nameof(DisposableResourceType.InstanceMethod))!, target: null!));
    }

    [Fact]
    public async Task UriTemplate_CreatedFromParameters_LotsOfTypesSupported()
    {
        const string Name = "Hello";

        McpServerResource t;
        ReadResourceResult result;
        McpServer server = new Mock<McpServer>().Object;

        t = McpServerResource.Create(() => "42", new() { Name = Name });
        Assert.Equal("resource://mcp/Hello", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Hello" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((McpServer server) => "42", new() { Name = Name });
        Assert.Equal("resource://mcp/Hello", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Hello" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((string arg1) => arg1, new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?arg1}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?arg1=wOrLd" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("wOrLd", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((string arg1, string? arg2 = null) => arg1 + arg2, new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?arg1,arg2}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?arg1=wo&arg2=rld" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("world", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((object a1, bool a2, char a3, byte a4, sbyte a5) => a1.ToString() + a2 + a3 + a4 + a5, new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a1,a2,a3,a4,a5}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a1=hi&a2=true&a3=s&a4=12&a5=34" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("hiTrues1234", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((ushort a1, short a2, uint a3, int a4, ulong a5) => (a1 + a2 + a3 + a4 + (long)a5).ToString(), new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a1,a2,a3,a4,a5}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a1=10&a2=20&a3=30&a4=40&a5=50" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("150", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((long a1, float a2, double a3, decimal a4, TimeSpan a5) => a5.ToString(), new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a1,a2,a3,a4,a5}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a1=1&a2=2&a3=3&a4=4&a5=5" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("5.00:00:00", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((DateTime a1, DateTimeOffset a2, Uri a3, Guid a4, Version a5) => a4.ToString("N") + a5, new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a1,a2,a3,a4,a5}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a1={DateTime.UtcNow:r}&a2={DateTimeOffset.UtcNow:r}&a3=http%3A%2F%2Ftest&a4=14e5f43d-0d41-47d6-8207-8249cf669e41&a5=1.2.3.4" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("14e5f43d0d4147d682078249cf669e411.2.3.4", ((TextResourceContents)result.Contents[0]).Text);

#if NET
        t = McpServerResource.Create((Half a2, Int128 a3, UInt128 a4, IntPtr a5) => (a3 + (Int128)a4 + a5).ToString(), new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a2,a3,a4,a5}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a2=1.0&a3=3&a4=4&a5=5" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("12", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((UIntPtr a1, DateOnly a2, TimeOnly a3) => a1.ToString(), new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a1,a2,a3}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a1=123&a2=0001-02-03&a3=01%3A02%3A03" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("123", ((TextResourceContents)result.Contents[0]).Text);
#endif

        t = McpServerResource.Create((bool? a2, char? a3, byte? a4, sbyte? a5) => a2?.ToString() + a3 + a4 + a5, new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a2,a3,a4,a5}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a2=true&a3=s&a4=12&a5=34" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("Trues1234", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((ushort? a1, short? a2, uint? a3, int? a4, ulong? a5) => (a1 + a2 + a3 + a4 + (long?)a5).ToString(), new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a1,a2,a3,a4,a5}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a1=10&a2=20&a3=30&a4=40&a5=50" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("150", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((long? a1, float? a2, double? a3, decimal? a4, TimeSpan? a5) => a5?.ToString(), new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a1,a2,a3,a4,a5}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a1=1&a2=2&a3=3&a4=4&a5=5" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("5.00:00:00", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((DateTime? a1, DateTimeOffset? a2, Guid? a4) => a4?.ToString("N"), new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a1,a2,a4}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a1={DateTime.UtcNow:r}&a2={DateTimeOffset.UtcNow:r}&a4=14e5f43d-0d41-47d6-8207-8249cf669e41" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("14e5f43d0d4147d682078249cf669e41", ((TextResourceContents)result.Contents[0]).Text);

#if NET
        t = McpServerResource.Create((Half? a2, Int128? a3, UInt128? a4, IntPtr? a5) => (a3 + (Int128?)a4 + a5).ToString(), new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a2,a3,a4,a5}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a2=1.0&a3=3&a4=4&a5=5" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("12", ((TextResourceContents)result.Contents[0]).Text);

        t = McpServerResource.Create((UIntPtr? a1, DateOnly? a2, TimeOnly? a3) => a1?.ToString(), new() { Name = Name });
        Assert.Equal($"resource://mcp/Hello{{?a1,a2,a3}}", t.ProtocolResourceTemplate.UriTemplate);
        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(server, CreateTestJsonRpcRequest()) { Params = new() { Uri = $"resource://mcp/Hello?a1=123&a2=0001-02-03&a3=01%3A02%3A03" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("123", ((TextResourceContents)result.Contents[0]).Text);
#endif
    }

    [Theory]
    [InlineData("resource://mcp/Hello?arg1=42&arg2=84")]
    [InlineData("resource://mcp/Hello?arg1=42&arg2=84&arg3=123")]
    [InlineData("resource://mcp/Hello#fragment")]
    public async Task UriTemplate_NonMatchingUri_DoesNotMatch(string uri)
    {
        McpServerResource t = McpServerResource.Create((string arg1) => arg1, new() { Name = "Hello" });
        Assert.Equal("resource://mcp/Hello{?arg1}", t.ProtocolResourceTemplate.UriTemplate);
        Assert.False(t.IsMatch(uri));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = uri } },
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("resource://MyCoolResource", "resource://mycoolresource")]
    [InlineData("resource://MyCoolResource{?arg1}", "resource://mycoolresource?arg1=42")]
    public async Task UriTemplate_IsHostCaseInsensitive(string actualUri, string queriedUri)
    {
        McpServerResource t = McpServerResource.Create(() => "resource", new() { UriTemplate = actualUri });
        Assert.NotNull(await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = queriedUri } },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResourceCollection_UsesCaseInsensitiveHostLookup()
    {
        McpServerResource t1 = McpServerResource.Create(() => "resource", new() { UriTemplate = "resource://MyCoolResource" });
        McpServerResource t2 = McpServerResource.Create(() => "resource", new() { UriTemplate = "resource://MyCoolResource2" });
        McpServerResourceCollection collection = [t1, t2];
        Assert.True(collection.TryGetPrimitive("resource://mycoolresource", out McpServerResource? result));
        Assert.Same(t1, result);
    }

    [Fact]
    public void MimeType_DefaultsToOctetStream()
    {
        McpServerResource t = McpServerResource.Create(() => "resource", new() { Name = "My Cool Resource" });
        Assert.Equal("application/octet-stream", t.ProtocolResourceTemplate.MimeType);
    }

    [Theory]
    [InlineData("resource://mcp/Hello?arg1=test")]
    [InlineData("resource://mcp/Hello?arg2=test")]
    public async Task UriTemplate_MissingParameter_Throws(string uri)
    {
        McpServerResource t = McpServerResource.Create((string arg1, int arg2) => arg1, new() { Name = "Hello" });
        Assert.Equal("resource://mcp/Hello{?arg1,arg2}", t.ProtocolResourceTemplate.UriTemplate);
        await Assert.ThrowsAsync<ArgumentException>(async () => await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = uri } },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UriTemplate_MissingOptionalParameter_Succeeds()
    {
        McpServerResource t = McpServerResource.Create((string? arg1 = null, int? arg2 = null) => arg1 + arg2, new() { Name = "Hello" });
        Assert.Equal("resource://mcp/Hello{?arg1,arg2}", t.ProtocolResourceTemplate.UriTemplate);

        ReadResourceResult result;

        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Hello" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("", ((TextResourceContents)result.Contents[0]).Text);

        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Hello?arg1=first" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("first", ((TextResourceContents)result.Contents[0]).Text);

        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Hello?arg2=42" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);

        result = await t.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Hello?arg1=first&arg2=42" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("first42", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task SupportsMcpServer()
    {
        Mock<McpServer> mockServer = new();

        McpServerResource resource = McpServerResource.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return "42";
        }, new() { Name = "Test" });

        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task SupportsCtorInjection()
    {
        MyService expectedMyService = new();

        ServiceCollection sc = new();
        sc.AddSingleton(expectedMyService);
        IServiceProvider services = sc.BuildServiceProvider();

        Mock<McpServer> mockServer = new();
        mockServer.SetupGet(s => s.Services).Returns(services);

        MethodInfo? testMethod = typeof(HasCtorWithSpecialParameters).GetMethod(nameof(HasCtorWithSpecialParameters.TestResource));
        Assert.NotNull(testMethod);
        McpServerResource tool = McpServerResource.Create(testMethod, r =>
        {
            Assert.NotNull(r.Services);
            return ActivatorUtilities.CreateInstance(r.Services, typeof(HasCtorWithSpecialParameters));
        }, new() { Services = services });

        var result = await tool.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "https://something" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Contents);
        Assert.Single(result.Contents);
        Assert.Equal("True True True True", Assert.IsType<TextResourceContents>(result.Contents[0]).Text);
    }

    private sealed class HasCtorWithSpecialParameters
    {
        private readonly MyService _ms;
        private readonly McpServer _server;
        private readonly RequestContext<ReadResourceRequestParams> _request;
        private readonly IProgress<ProgressNotificationValue> _progress;

        public HasCtorWithSpecialParameters(MyService ms, McpServer server, RequestContext<ReadResourceRequestParams> request, IProgress<ProgressNotificationValue> progress)
        {
            Assert.NotNull(ms);
            Assert.NotNull(server);
            Assert.NotNull(request);
            Assert.NotNull(progress);

            _ms = ms;
            _server = server;
            _request = request;
            _progress = progress;
        }

        [McpServerResource(UriTemplate = "https://something")]
        public string TestResource() => $"{_ms is not null} {_server is not null} {_request is not null} {_progress is not null}";
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
            }, new() { Services = services, Name = "Test" });
        });

        IServiceProvider services = sc.BuildServiceProvider();

        McpServerResource resource = services.GetRequiredService<McpServerResource>();

        Mock<McpServer> mockServer = new();

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken));

        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Services = services, Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
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
        }, new() { Services = services, Name = "Test" });

        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task SupportsDisposingInstantiatedDisposableTargets()
    {
        int before = DisposableResourceType.Disposals;

        McpServerResource resource1 = McpServerResource.Create(
            typeof(DisposableResourceType).GetMethod(nameof(DisposableResourceType.InstanceMethod))!,
            _ => new DisposableResourceType());

        var result = await resource1.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(new Mock<McpServer>().Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "test://static/resource/instanceMethod" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("0", ((TextResourceContents)result.Contents[0]).Text);

        Assert.Equal(1, DisposableResourceType.Disposals);
    }

    [Fact]
    public async Task CanReturnReadResult()
    {
        Mock<McpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new ReadResourceResult { Contents = [new TextResourceContents { Text = "hello", Uri = "" }] };
        }, new() { Name = "Test" });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Contents);
        Assert.Equal("hello", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task CanReturnResourceContents()
    {
        Mock<McpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new TextResourceContents { Text = "hello", Uri = "" };
        }, new() { Name = "Test", SerializerOptions = JsonContext6.Default.Options });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Contents);
        Assert.Equal("hello", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task CanReturnCollectionOfResourceContents()
    {
        Mock<McpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return (IList<ResourceContents>)
            [
                new TextResourceContents { Text = "hello", Uri = "" },
                BlobResourceContents.FromBytes((byte[])[1, 2, 3], ""),
            ];
        }, new() { Name = "Test" });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(2, result.Contents.Count);
        Assert.Equal("hello", ((TextResourceContents)result.Contents[0]).Text);
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), System.Text.Encoding.UTF8.GetString(((BlobResourceContents)result.Contents[1]).Blob.ToArray()));
    }

    [Fact]
    public async Task CanReturnString()
    {
        Mock<McpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return "42";
        }, new() { Name = "Test" });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Contents);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);
    }

    [Fact]
    public async Task CanReturnCollectionOfStrings()
    {
        Mock<McpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new List<string> { "42", "43" };
        }, new() { Name = "Test", SerializerOptions = JsonContext6.Default.Options });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(2, result.Contents.Count);
        Assert.Equal("42", ((TextResourceContents)result.Contents[0]).Text);
        Assert.Equal("43", ((TextResourceContents)result.Contents[1]).Text);
    }

    [Fact]
    public async Task CanReturnDataContent()
    {
        Mock<McpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new DataContent(new byte[] { 0, 1, 2 }, "application/octet-stream");
        }, new() { Name = "Test" });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Contents);
        Assert.Equal(Convert.ToBase64String(new byte[] { 0, 1, 2 }), System.Text.Encoding.UTF8.GetString(((BlobResourceContents)result.Contents[0]).Blob.ToArray()));
        Assert.Equal("application/octet-stream", ((BlobResourceContents)result.Contents[0]).MimeType);
    }

    [Fact]
    public async Task CanReturnCollectionOfAIContent()
    {
        Mock<McpServer> mockServer = new();
        McpServerResource resource = McpServerResource.Create((McpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new List<AIContent>
            {
                new TextContent("hello!"),
                new DataContent(new byte[] { 4, 5, 6 }, "application/json"),
            };
        }, new() { Name = "Test", SerializerOptions = JsonContext6.Default.Options });
        var result = await resource.ReadAsync(
            new RequestContext<ReadResourceRequestParams>(mockServer.Object, CreateTestJsonRpcRequest()) { Params = new() { Uri = "resource://mcp/Test" } },
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(2, result.Contents.Count);
        Assert.Equal("hello!", ((TextResourceContents)result.Contents[0]).Text);
        Assert.Equal(Convert.ToBase64String(new byte[] { 4, 5, 6 }), System.Text.Encoding.UTF8.GetString(((BlobResourceContents)result.Contents[1]).Blob.ToArray()));
        Assert.Equal("application/json", ((BlobResourceContents)result.Contents[1]).MimeType);
    }

    private sealed class MyService;

    private class DisposableResourceType : IDisposable
    {
        public static int Disposals { get; private set; }

        public void Dispose() => Disposals++;

        [McpServerResource(UriTemplate = "test://static/resource/instanceMethod")]
        public object InstanceMethod() => Disposals.ToString();

        [McpServerResource(UriTemplate = "test://static/resource/staticMethod")]
        public static object StaticMethod() => "42";
    }

    [Fact]
    public void SupportsIconsInResourceCreateOptions()
    {
        var icons = new List<Icon>
        {
            new() { Source = "https://example.com/resource-icon.png", MimeType = "image/png", Sizes = ["32x32"] }
        };

        McpServerResource resource = McpServerResource.Create(() => "test content", new McpServerResourceCreateOptions
        {
            UriTemplate = "test://resource/with-icon",
            Icons = icons
        });

        var icon = Assert.Single(resource.ProtocolResourceTemplate.Icons!);
        Assert.Equal("https://example.com/resource-icon.png", icon.Source);
        Assert.Equal("image/png", icon.MimeType);
    }

    [Fact]
    public void SupportsIconSourceInResourceAttribute()
    {
        McpServerResource resource = McpServerResource.Create([McpServerResource(UriTemplate = "test://resource", IconSource = "https://example.com/resource-icon.svg")] () => "test content");

        var icon = Assert.Single(resource.ProtocolResourceTemplate.Icons!);
        Assert.Equal("https://example.com/resource-icon.svg", icon.Source);
        Assert.Null(icon.MimeType);
        Assert.Null(icon.Sizes);
    }

    [Fact]
    public void CreateOptionsIconsOverrideAttributeIconSource_Resource()
    {
        var optionsIcons = new List<Icon>
        {
            new() { Source = "https://example.com/override-icon.svg", MimeType = "image/svg+xml" }
        };

        McpServerResource resource = McpServerResource.Create([McpServerResource(UriTemplate = "test://resource", IconSource = "https://example.com/resource-icon.png")] () => "test content", new McpServerResourceCreateOptions
        {
            Icons = optionsIcons
        });

        var icon = Assert.Single(resource.ProtocolResourceTemplate.Icons!);
        Assert.Equal("https://example.com/override-icon.svg", icon.Source);
        Assert.Equal("image/svg+xml", icon.MimeType);
    }

    [Fact]
    public void SupportsResourceWithoutIcons()
    {
        McpServerResource resource = McpServerResource.Create([McpServerResource(UriTemplate = "test://resource")] () => "test content");

        Assert.Null(resource.ProtocolResourceTemplate.Icons);
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DisposableResourceType))]
    [JsonSerializable(typeof(List<AIContent>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(TextResourceContents))]
    partial class JsonContext6 : JsonSerializerContext;
}
