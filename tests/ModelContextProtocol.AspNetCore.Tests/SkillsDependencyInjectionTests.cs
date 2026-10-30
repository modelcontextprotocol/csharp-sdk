using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.AspNetCore.Tests;

public class SkillsDependencyInjectionTests(ITestOutputHelper output) : KestrelInMemoryTest(output)
{
    private const string SkillUri = "skill://database/SKILL.md";
    private const string MiddlewareState = "From HTTP middleware";
    private const string McpRequestState = "Independent MCP request scope";

    [Theory]
    [InlineData(false, HttpServerSessionMode.Stateless, false, "2026-07-28")]
    [InlineData(true, HttpServerSessionMode.Stateless, false, "2026-07-28")]
    [InlineData(false, HttpServerSessionMode.Stateless, false, "2025-11-25")]
    [InlineData(true, HttpServerSessionMode.Stateless, false, "2025-11-25")]
    [InlineData(false, HttpServerSessionMode.StatefulForInitializeClients, false, "2026-07-28")]
    [InlineData(false, HttpServerSessionMode.StatefulForInitializeClients, false, "2025-11-25")]
    [InlineData(false, HttpServerSessionMode.Stateful, false, "2025-11-25")]
    [InlineData(false, HttpServerSessionMode.Stateful, true, "2025-11-25")]
    [InlineData(false, HttpServerSessionMode.Stateful, true, "2026-07-28")]
    public async Task Catalog_UsesTheTransportRequestScope(
        bool factory, HttpServerSessionMode mode, bool useSse, string protocolVersion)
    {
        var tracker = new InvocationTracker();
        await using var app = await StartServerAsync(tracker, factory, mode, useSse);
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new Uri(useSse ? "http://localhost:5000/sse" : "http://localhost:5000/"),
            TransportMode = useSse ? HttpTransportMode.Sse : HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);
        var clientOptions = TestMcpClient.CreateOptions();
        clientOptions.ProtocolVersion = protocolVersion;
        await using var client = await TestMcpClient.CreateAsync(
            transport, clientOptions, LoggerFactory, TestContext.Current.CancellationToken);
        Assert.Empty(tracker.Invocations);
        bool usesHttpScope = !useSse && (mode == HttpServerSessionMode.Stateless ||
            mode == HttpServerSessionMode.StatefulForInitializeClients && protocolVersion == McpProtocolVersions.July2026ProtocolVersion);

        var listed = Assert.Single(await client.ListSkillsAsync(TestContext.Current.CancellationToken));
        var retrieved = await client.GetSkillAsync(SkillUri, TestContext.Current.CancellationToken);

        Assert.Equal(usesHttpScope ? MiddlewareState : McpRequestState, listed.Description);
        Assert.Equal(listed.Description, retrieved.Description);
        var invocations = tracker.Invocations.ToArray();
        Assert.Equal(2, invocations.Length);
        Assert.NotSame(invocations[0].Catalog, invocations[1].Catalog);
        Assert.NotSame(invocations[0].Dependency, invocations[1].Dependency);
        foreach (var invocation in invocations)
        {
            if (usesHttpScope)
            {
                Assert.Same(invocation.Dependency.HttpRequestServices, invocation.Services);
            }
            else
            {
                Assert.Null(invocation.Dependency.HttpRequestServices);
            }
            await Task.WhenAll(invocation.Dependency.Disposed.Task, invocation.Catalog.Disposed.Task)
                .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            Assert.Equal(1, invocation.Dependency.DisposeCount);
            Assert.Equal(1, invocation.Catalog.DisposeCount);
        }
    }

    [Fact]
    public async Task CatalogFailure_DisposesHttpOwnedCatalogAndDependencies()
    {
        var tracker = new InvocationTracker { Fail = true };
        await using var app = await StartServerAsync(tracker, factory: false, HttpServerSessionMode.Stateless, useSse: false);
        await using var transport = new HttpClientTransport(
            new() { Endpoint = new Uri("http://localhost:5000/") }, HttpClient, LoggerFactory);
        await using var client = await TestMcpClient.CreateAsync(
            transport, cancellationToken: TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InternalError, exception.ErrorCode);
        var invocation = Assert.Single(tracker.Invocations);
        await Task.WhenAll(invocation.Dependency.Disposed.Task, invocation.Catalog.Disposed.Task)
            .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, invocation.Dependency.DisposeCount);
        Assert.Equal(1, invocation.Catalog.DisposeCount);
    }

    private async Task<WebApplication> StartServerAsync(
        InvocationTracker tracker, bool factory, HttpServerSessionMode mode, bool useSse)
    {
        Builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        Builder.Services.AddSingleton(tracker);
        Builder.Services.AddScoped<RequestDependency>();
        var mcpBuilder = Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode = mode;
                options.EnableLegacySse = useSse;
            })
            .WithMessageFilters(filters => filters.AddIncomingFilter(next => (context, cancellationToken) =>
            {
                context.Items["source"] = "incoming filter";
                return next(context, cancellationToken);
            }));
        if (factory)
        {
            Builder.Services.AddScoped(services => new ScopedCatalog(
                services.GetRequiredService<RequestDependency>(), services.GetRequiredService<InvocationTracker>()));
        }
        else
        {
            Builder.Services.AddScoped<ScopedCatalog>();
        }
        mcpBuilder.WithSkills(options =>
        {
            options.ListSkillsHandler = (context, cancellationToken) =>
                context.Services!.GetRequiredService<ScopedCatalog>().ListAsync(context, cancellationToken);
            options.GetSkillHandler = (context, cancellationToken) =>
                context.Services!.GetRequiredService<ScopedCatalog>().GetAsync(context, cancellationToken);
        });

        var app = Builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "reader")], "Test"));
            var dependency = context.RequestServices.GetRequiredService<RequestDependency>();
            dependency.State = MiddlewareState;
            dependency.HttpRequestServices = context.RequestServices;
            await next(context);
        });
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class InvocationTracker
    {
        public ConcurrentQueue<Invocation> Invocations { get; } = new();
        public bool Fail { get; init; }
    }

    private sealed record Invocation(ScopedCatalog Catalog, RequestDependency Dependency, IServiceProvider Services);

    private sealed class RequestDependency : IAsyncDisposable
    {
        public string? State { get; set; }
        public IServiceProvider? HttpRequestServices { get; set; }
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            DisposeCount++;
            Disposed.TrySetResult();
        }
    }

    private sealed class ScopedCatalog(RequestDependency dependency, InvocationTracker tracker) : IAsyncDisposable
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }

        public ValueTask<ListSkillsResult> ListAsync(RequestContext<ListSkillsRequestParams> context, CancellationToken cancellationToken)
        {
            Record(context);
            return new(new ListSkillsResult { Skills = [CreateEntry()] });
        }

        public ValueTask<GetSkillResult> GetAsync(RequestContext<GetSkillRequestParams> context, CancellationToken cancellationToken)
        {
            Record(context);
            return new(new GetSkillResult
            {
                Skill = context.Params.Uri == SkillUri ? CreateEntry() :
                    throw new McpProtocolException("No skill is served.", McpErrorCode.InvalidParams),
            });
        }

        private void Record(MessageContext context)
        {
            var services = Assert.IsAssignableFrom<IServiceProvider>(context.Services);
            Assert.Same(dependency, services.GetRequiredService<RequestDependency>());
            Assert.Equal("reader", context.User?.Identity?.Name);
            Assert.Equal("incoming filter", context.Items["source"]);
            Assert.NotNull(context.Server);
            tracker.Invocations.Enqueue(new(this, dependency, services));
            if (tracker.Fail)
            {
                throw new McpProtocolException("Catalog failed.", McpErrorCode.InternalError);
            }
        }

        private Skill CreateEntry() => new()
        {
            Uri = SkillUri,
            Frontmatter = new JsonObject { ["name"] = "database", ["description"] = dependency.State ?? McpRequestState },
            Resources = SkillResources.Dynamic,
        };

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            DisposeCount++;
            Disposed.TrySetResult();
        }
    }
}
