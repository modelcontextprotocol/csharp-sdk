using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore.Tests;

public class HttpMcpServerBuilderExtensionsTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    [Fact]
    public void WithDistributedCacheEventStreamStore_RegistersStoreInDI()
    {
        Builder.Services.AddDistributedMemoryCache();
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithDistributedCacheEventStreamStore();

        using var app = Builder.Build();

        var store = app.Services.GetService<ISseEventStreamStore>();
        Assert.IsType<DistributedCacheEventStreamStore>(store);
    }

    [Fact]
    public void WithDistributedCacheEventStreamStore_ConfigureCallbackIsInvoked()
    {
        DistributedCacheEventStreamStoreOptions? capturedOptions = null;

        Builder.Services.AddDistributedMemoryCache();
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithDistributedCacheEventStreamStore(options => capturedOptions = options);

        using var app = Builder.Build();

        // Force options resolution to trigger the configure callback.
        _ = app.Services.GetRequiredService<IOptions<DistributedCacheEventStreamStoreOptions>>().Value;

        Assert.NotNull(capturedOptions);
    }

    [Fact]
    public void WithDistributedCacheEventStreamStore_WorksWithoutDICache_WhenCacheSetViaCallback()
    {
        var explicitCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        Builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithDistributedCacheEventStreamStore(options => options.Cache = explicitCache);

        using var app = Builder.Build();

        var store = app.Services.GetService<ISseEventStreamStore>();
        Assert.IsType<DistributedCacheEventStreamStore>(store);
    }

    [Fact]
    public void WithDistributedCacheEventStreamStore_ThrowsOptionsValidationException_WhenNoCacheConfigured()
    {
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithDistributedCacheEventStreamStore();

        using var app = Builder.Build();

        var ex = Assert.Throws<OptionsValidationException>(
            () => app.Services.GetRequiredService<ISseEventStreamStore>());
        Assert.StartsWith($"The '{nameof(DistributedCacheEventStreamStoreOptions)}.{nameof(DistributedCacheEventStreamStoreOptions.Cache)}'", ex.Message);
    }

    [Fact]
    public void EventStreamStore_IsPopulatedFromDI_ViaPostConfigure()
    {
        Builder.Services.AddDistributedMemoryCache();
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithDistributedCacheEventStreamStore();

        using var app = Builder.Build();

        var options = app.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        Assert.IsType<DistributedCacheEventStreamStore>(options.EventStreamStore);
    }

    [Fact]
    public void EventStreamStore_ExplicitOption_TakesPrecedenceOverDI()
    {
        var explicitStore = new TestSseEventStreamStore();

        Builder.Services.AddDistributedMemoryCache();
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.EventStreamStore = explicitStore)
            .WithDistributedCacheEventStreamStore();

        using var app = Builder.Build();

        var options = app.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        Assert.Same(explicitStore, options.EventStreamStore);
    }

    [Fact]
    public void EventStreamStore_RemainsNull_WhenNothingIsRegistered()
    {
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport();

        using var app = Builder.Build();

        var options = app.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        Assert.Null(options.EventStreamStore);
    }

    [Fact]
    public void EventStreamStore_CanBeOverriddenToNull_AfterDIRegistration()
    {
        Builder.Services.AddDistributedMemoryCache();
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithDistributedCacheEventStreamStore();

        Builder.Services.Configure<HttpServerTransportOptions>(options => options.EventStreamStore = null);

        using var app = Builder.Build();

        var options = app.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        Assert.Null(options.EventStreamStore);
    }

    [Fact]
    public void SessionMigrationHandler_IsPopulatedFromDI_ViaPostConfigure()
    {
        var handler = new StubSessionMigrationHandler();

        Builder.Services.AddSingleton<ISessionMigrationHandler>(handler);
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport();

        using var app = Builder.Build();

        var options = app.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        Assert.Same(handler, options.SessionMigrationHandler);
    }

    [Fact]
    public void SessionMigrationHandler_ExplicitOption_TakesPrecedenceOverDI()
    {
        var diHandler = new StubSessionMigrationHandler();
        var explicitHandler = new StubSessionMigrationHandler();

        Builder.Services.AddSingleton<ISessionMigrationHandler>(diHandler);
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.SessionMigrationHandler = explicitHandler);

        using var app = Builder.Build();

        var options = app.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        Assert.Same(explicitHandler, options.SessionMigrationHandler);
    }

    [Fact]
    public void SessionMigrationHandler_RemainsNull_WhenNothingIsRegistered()
    {
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport();

        using var app = Builder.Build();

        var options = app.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        Assert.Null(options.SessionMigrationHandler);
    }

    private sealed class StubSessionMigrationHandler : ISessionMigrationHandler
    {
        public ValueTask<InitializeRequestParams?> AllowSessionMigrationAsync(
            HttpContext context, string sessionId, CancellationToken cancellationToken = default)
            => new((InitializeRequestParams?)null);

        public ValueTask OnSessionInitializedAsync(
            HttpContext context, string sessionId, InitializeRequestParams initializeParams, CancellationToken cancellationToken = default)
            => default;
    }
}
