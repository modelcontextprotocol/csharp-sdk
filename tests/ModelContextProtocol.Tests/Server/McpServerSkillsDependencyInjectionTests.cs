using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpServerSkillsDependencyInjectionTests : ClientServerTestBase
{
    private const string SkillUri = "skill://database/SKILL.md";
    private readonly CatalogTracker _tracker = new();

    public McpServerSkillsDependencyInjectionTests(ITestOutputHelper output)
        : base(output, startServer: false)
    {
        ServiceCollection.AddSingleton(_tracker);
        ServiceCollection.AddScoped<RequestDependency>();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Assert.All(_tracker.Catalogs, catalog => Assert.Equal(1, catalog.DisposeCount));
        Assert.All(_tracker.Dependencies, dependency => Assert.Equal(1, dependency.DisposeCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultLifetime_IsTransient_AndReusesScopedDependencies(bool factory)
    {
        if (factory)
        {
            McpServerBuilder.WithSkills(services => new ScopedCatalog(
                services.GetRequiredService<RequestDependency>(), services.GetRequiredService<CatalogTracker>()));
        }
        else
        {
            McpServerBuilder.WithSkills<ScopedCatalog>();
        }
        _tracker.OnRequest = (invocation, _) =>
        {
            var other = Assert.IsType<ScopedCatalog>(invocation.Services.GetRequiredService<IMcpSkillCatalog>());
            Assert.NotSame(invocation.Catalog, other);
            Assert.Same(invocation.Dependency, other.Dependency);
            return default;
        };
        StartServer();
        await using var client = await CreateMcpClientForServer();
        Assert.Empty(_tracker.Catalogs);

        await client.ListSkillsAsync(TestContext.Current.CancellationToken);
        await client.GetSkillAsync(SkillUri, TestContext.Current.CancellationToken);

        Assert.Equal(4, _tracker.Catalogs.Count);
        Assert.Equal(2, _tracker.Dependencies.Count);
        Assert.All(_tracker.Catalogs, catalog => Assert.Equal(1, catalog.DisposeCount));
        Assert.All(_tracker.Dependencies, dependency => Assert.Equal(1, dependency.DisposeCount));
    }

    [Theory]
    [InlineData(false, ServiceLifetime.Singleton)]
    [InlineData(false, ServiceLifetime.Scoped)]
    [InlineData(false, ServiceLifetime.Transient)]
    [InlineData(true, ServiceLifetime.Singleton)]
    [InlineData(true, ServiceLifetime.Scoped)]
    [InlineData(true, ServiceLifetime.Transient)]
    public async Task Catalog_IsResolvedAtInvocation_WithTheConfiguredLifetime(bool factory, ServiceLifetime lifetime)
    {
        var timeToLive = TimeSpan.FromMinutes(1);
        Action<McpSkillsOptions> configure = options => options.TimeToLive = timeToLive;
        if (factory)
        {
            McpServerBuilder.WithSkills(services => new TestCatalog(services.GetRequiredService<CatalogTracker>()), lifetime, configure);
        }
        else
        {
            McpServerBuilder.WithSkills<TestCatalog>(lifetime, configure);
        }
        _tracker.OnRequest = (invocation, _) =>
        {
            var resolved = invocation.Services.GetRequiredService<IMcpSkillCatalog>();
            if (lifetime == ServiceLifetime.Transient)
            {
                Assert.NotSame(invocation.Catalog, resolved);
            }
            else
            {
                Assert.Same(invocation.Catalog, resolved);
            }
            return default;
        };
        StartServer();
        await using var client = await CreateMcpClientForServer();
        Assert.Empty(_tracker.Catalogs);

        var list = await client.ListSkillsAsync(new ListSkillsRequestParams(), TestContext.Current.CancellationToken);
        var get = await client.GetSkillAsync(new GetSkillRequestParams { Uri = SkillUri }, TestContext.Current.CancellationToken);

        Assert.Equal(timeToLive, list.TimeToLive);
        Assert.Equal(timeToLive, get.TimeToLive);
        var invocations = _tracker.Invocations.ToArray();
        Assert.Equal(2, invocations.Length);
        Assert.NotSame(invocations[0].Dependency, invocations[1].Dependency);
        Assert.All(invocations, invocation =>
        {
            Assert.NotSame(Server.Services, invocation.Services);
            Assert.Equal(1, invocation.Dependency.DisposeCount);
        });
        Assert.Equal(lifetime switch
        {
            ServiceLifetime.Singleton => 1,
            ServiceLifetime.Scoped => 2,
            _ => 4,
        }, _tracker.Catalogs.Count);
        Assert.All(_tracker.Catalogs, catalog => Assert.Equal(lifetime == ServiceLifetime.Singleton ? 0 : 1, catalog.DisposeCount));
    }

    [Theory]
    [InlineData(false, ServiceLifetime.Scoped)]
    [InlineData(false, ServiceLifetime.Transient)]
    [InlineData(true, ServiceLifetime.Scoped)]
    [InlineData(true, ServiceLifetime.Transient)]
    public async Task ConstructorAndFactoryDependencies_ComeFromTheSameRequestScope(bool factory, ServiceLifetime lifetime)
    {
        if (factory)
        {
            McpServerBuilder.WithSkills(services => new ScopedCatalog(
                services.GetRequiredService<RequestDependency>(), services.GetRequiredService<CatalogTracker>()), lifetime);
        }
        else
        {
            McpServerBuilder.WithSkills<ScopedCatalog>(lifetime);
        }
        StartServer();
        await using var client = await CreateMcpClientForServer();

        await client.ListSkillsAsync(TestContext.Current.CancellationToken);
        await client.GetSkillAsync(SkillUri, TestContext.Current.CancellationToken);

        Assert.Equal(2, _tracker.Catalogs.Count);
        Assert.Equal(2, _tracker.Dependencies.Count);
        Assert.All(_tracker.Catalogs, catalog => Assert.Equal(1, catalog.DisposeCount));
        Assert.All(_tracker.Dependencies, dependency => Assert.Equal(1, dependency.DisposeCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingletonRegistration_DoesNotAllowCaptiveScopedDependencies(bool factory)
    {
        if (factory)
        {
            McpServerBuilder.WithSkills(services => new ScopedCatalog(
                services.GetRequiredService<RequestDependency>(), services.GetRequiredService<CatalogTracker>()), ServiceLifetime.Singleton);
        }
        else
        {
            McpServerBuilder.WithSkills<ScopedCatalog>(ServiceLifetime.Singleton);
        }
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InternalError, exception.ErrorCode);
        Assert.Empty(_tracker.Catalogs);
        Assert.Empty(_tracker.Dependencies);
    }

    [Fact]
    public async Task FactoryFailure_DisposesDependenciesCreatedBeforeTheFailure()
    {
        McpServerBuilder.WithSkills(services =>
        {
            services.GetRequiredService<RequestDependency>();
            throw new InvalidOperationException("Catalog construction failed.");
        });
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InternalError, exception.ErrorCode);
        Assert.Empty(_tracker.Catalogs);
        Assert.Equal(1, Assert.Single(_tracker.Dependencies).DisposeCount);
    }

    [Fact]
    public async Task SuppliedInstance_IsSharedAndCallerOwned_ButCanUseRequestServices()
    {
        await using var catalog = new TestCatalog(_tracker);
        McpServerBuilder.WithSkills(catalog);
        _tracker.OnRequest = (invocation, _) =>
        {
            Assert.Same(catalog, invocation.Services.GetRequiredService<IMcpSkillCatalog>());
            return default;
        };
        StartServer();
        await using var client = await CreateMcpClientForServer();

        await client.ListSkillsAsync(TestContext.Current.CancellationToken);
        await client.GetSkillAsync(SkillUri, TestContext.Current.CancellationToken);

        Assert.Same(catalog, Assert.Single(_tracker.Catalogs));
        Assert.Equal(0, catalog.DisposeCount);
        Assert.Equal(2, _tracker.Dependencies.Count);
        Assert.All(_tracker.Dependencies, dependency => Assert.Equal(1, dependency.DisposeCount));
    }

    [Fact]
    public async Task Catalog_ReceivesTypedParametersAndRequestMetadata()
    {
        McpServerBuilder.WithSkills<TestCatalog>();
        StartServer();
        await using var client = await CreateMcpClientForServer();

        await client.ListSkillsAsync(new ListSkillsRequestParams
        {
            Cursor = "next-page",
            Meta = new JsonObject { ["example.com/request"] = "list" },
        }, TestContext.Current.CancellationToken);
        await client.GetSkillAsync(new GetSkillRequestParams
        {
            Uri = SkillUri,
            Meta = new JsonObject { ["example.com/request"] = "get" },
        }, TestContext.Current.CancellationToken);

        var invocations = _tracker.Invocations.ToArray();
        Assert.Equal(2, invocations.Length);
        var list = Assert.IsType<RequestContext<ListSkillsRequestParams>>(invocations[0].Context);
        var get = Assert.IsType<RequestContext<GetSkillRequestParams>>(invocations[1].Context);
        Assert.Equal("next-page", list.Params.Cursor);
        Assert.Equal(SkillUri, get.Params.Uri);
        Assert.Equal("list", list.Params.Meta?["example.com/request"]?.GetValue<string>());
        Assert.Equal("get", get.Params.Meta?["example.com/request"]?.GetValue<string>());
        Assert.Equal(SkillsProtocol.MethodSkillsList, list.JsonRpcRequest.Method);
        Assert.Equal(SkillsProtocol.MethodSkillsGet, get.JsonRpcRequest.Method);
        Assert.Same(invocations[0].Services, list.Services);
        Assert.Same(invocations[1].Services, get.Services);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestScope_AwaitsAsyncOnlyDisposal_OnSuccessAndFailure(bool fail)
    {
        var allowDisposal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tracker.AllowDependencyDisposal = allowDisposal.Task;
        if (fail)
        {
            _tracker.OnRequest = (_, _) => throw new McpProtocolException("Catalog failed.", McpErrorCode.InternalError);
        }
        McpServerBuilder.WithSkills<ScopedCatalog>();
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var request = client.ListSkillsAsync(TestContext.Current.CancellationToken).AsTask();
        try
        {
            await _tracker.DependencyDisposalStarted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            Assert.False(request.IsCompleted);
        }
        finally
        {
            allowDisposal.TrySetResult(true);
        }

        if (fail)
        {
            var exception = await Assert.ThrowsAsync<McpProtocolException>(async () => await request);
            Assert.Equal(McpErrorCode.InternalError, exception.ErrorCode);
        }
        else
        {
            Assert.Single(await request);
        }
        Assert.Equal(1, Assert.Single(_tracker.Catalogs).DisposeCount);
        Assert.Equal(1, Assert.Single(_tracker.Dependencies).DisposeCount);
    }

    [Fact]
    public async Task CanceledRequest_DisposesTheCatalogAndItsDependencies()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitForCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tracker.OnRequest = async (_, cancellationToken) =>
        {
            entered.SetResult(true);
            await waitForCancellation.Task.WaitAsync(cancellationToken);
        };
        McpServerBuilder.WithSkills<ScopedCatalog>();
        StartServer();
        await using var client = await CreateMcpClientForServer();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var request = client.ListSkillsAsync(cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
        await Task.WhenAll(_tracker.CatalogDisposed.Task, _tracker.DependencyDisposed.Task)
            .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(_tracker.Catalogs).DisposeCount);
        Assert.Equal(1, Assert.Single(_tracker.Dependencies).DisposeCount);
    }

    [Fact]
    public async Task ConcurrentRequests_DoNotShareCatalogsOrScopedDependencies()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        _tracker.OnRequest = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                entered.SetResult(true);
            }
            await release.Task.WaitAsync(cancellationToken);
        };
        McpServerBuilder.WithSkills<ScopedCatalog>();
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var first = client.ListSkillsAsync(TestContext.Current.CancellationToken).AsTask();
        var second = client.GetSkillAsync(SkillUri, TestContext.Current.CancellationToken).AsTask();
        try
        {
            await entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            var invocations = _tracker.Invocations.ToArray();
            Assert.Equal(2, invocations.Length);
            Assert.NotSame(invocations[0].Catalog, invocations[1].Catalog);
            Assert.NotSame(invocations[0].Dependency, invocations[1].Dependency);
            Assert.All(_tracker.Catalogs, catalog => Assert.Equal(0, catalog.DisposeCount));
            Assert.All(_tracker.Dependencies, dependency => Assert.Equal(0, dependency.DisposeCount));
        }
        finally
        {
            release.TrySetResult(true);
        }
        await Task.WhenAll(first, second);
    }

    private sealed class CatalogTracker
    {
        public ConcurrentQueue<TestCatalog> Catalogs { get; } = new();
        public ConcurrentQueue<RequestDependency> Dependencies { get; } = new();
        public ConcurrentQueue<Invocation> Invocations { get; } = new();
        public Func<Invocation, CancellationToken, ValueTask>? OnRequest { get; set; }
        public Task? AllowDependencyDisposal { get; set; }
        public TaskCompletionSource<bool> DependencyDisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> DependencyDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CatalogDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record Invocation(TestCatalog Catalog, RequestDependency Dependency, IServiceProvider Services, MessageContext Context);

    private sealed class RequestDependency : IAsyncDisposable
    {
        private readonly CatalogTracker _tracker;
        public int DisposeCount { get; private set; }

        public RequestDependency(CatalogTracker tracker)
        {
            _tracker = tracker;
            tracker.Dependencies.Enqueue(this);
        }

        public async ValueTask DisposeAsync()
        {
            _tracker.DependencyDisposalStarted.TrySetResult(true);
            if (_tracker.AllowDependencyDisposal is { } allowDisposal)
            {
                await allowDisposal;
            }
            DisposeCount++;
            _tracker.DependencyDisposed.TrySetResult(true);
        }
    }

    private class TestCatalog : IMcpSkillCatalog, IAsyncDisposable
    {
        private readonly CatalogTracker _tracker;
        public int DisposeCount { get; private set; }

        public TestCatalog(CatalogTracker tracker)
        {
            _tracker = tracker;
            tracker.Catalogs.Enqueue(this);
        }

        public async ValueTask<McpSkillPage> ListAsync(RequestContext<ListSkillsRequestParams> context, CancellationToken cancellationToken)
        {
            await RecordAsync(context, cancellationToken);
            return new McpSkillPage { Skills = [CreateEntry()] };
        }

        public async ValueTask<Skill?> GetAsync(RequestContext<GetSkillRequestParams> context, CancellationToken cancellationToken)
        {
            await RecordAsync(context, cancellationToken);
            return context.Params.Uri == SkillUri ? CreateEntry() : null;
        }

        protected virtual void ValidateDependency(RequestDependency dependency)
        {
        }

        private async ValueTask RecordAsync(MessageContext context, CancellationToken cancellationToken)
        {
            Assert.NotNull(context.Server);
            var services = Assert.IsAssignableFrom<IServiceProvider>(context.Services);
            var dependency = services.GetRequiredService<RequestDependency>();
            ValidateDependency(dependency);
            var invocation = new Invocation(this, dependency, services, context);
            _tracker.Invocations.Enqueue(invocation);
            if (_tracker.OnRequest is { } onRequest)
            {
                await onRequest(invocation, cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            DisposeCount++;
            _tracker.CatalogDisposed.TrySetResult(true);
        }

        private static Skill CreateEntry() => new()
        {
            Uri = SkillUri,
            Frontmatter = new JsonObject { ["name"] = "database", ["description"] = "Database-backed skill" },
            Resources = SkillResources.Dynamic,
        };
    }

    private sealed class ScopedCatalog(RequestDependency dependency, CatalogTracker tracker) : TestCatalog(tracker)
    {
        public RequestDependency Dependency => dependency;

        protected override void ValidateDependency(RequestDependency resolvedDependency) => Assert.Same(dependency, resolvedDependency);
    }
}
