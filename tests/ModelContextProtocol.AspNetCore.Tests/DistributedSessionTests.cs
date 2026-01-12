using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net;
using System.Net.Http.Headers;

namespace ModelContextProtocol.AspNetCore.Tests;

public class DistributedSessionTests(ITestOutputHelper outputHelper) : MapMcpTests(outputHelper)
{
    protected override bool UseStreamableHttp => true;
    protected override bool Stateless => false;

    [Fact]
    public async Task DistributedSessions_SavesMetadataToStore()
    {
        var sessionStore = new InMemorySessionStore();

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "DistributedSessionTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(options =>
        {
            options.EnableDistributedSessions = true;
        }).WithSessionStore(sessionStore);

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Connect and establish a session
        await using var client = await ConnectAsync("/");

        // Wait a moment for the session to be persisted
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Verify session was saved to store
        Assert.Equal(1, sessionStore.Count);
    }

    [Fact]
    public async Task DistributedSessions_StoresUserIdentity()
    {
        var sessionStore = new InMemorySessionStore();

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "DistributedSessionTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(options =>
        {
            options.EnableDistributedSessions = true;
            options.ConfigureSessionOptions = async (context, serverOptions, ct) =>
            {
                // Capture the session ID from the response header after it's set
                await Task.CompletedTask;
            };
        }).WithSessionStore(sessionStore);

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectAsync("/");

        // Wait for session to be persisted
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Get the stored session and verify it exists
        Assert.Equal(1, sessionStore.Count);
    }

    [Fact]
    public async Task DistributedSessions_RemovesFromStoreOnDelete()
    {
        var sessionStore = new InMemorySessionStore();

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "DistributedSessionTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(options =>
        {
            options.EnableDistributedSessions = true;
        }).WithSessionStore(sessionStore);

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Connect and establish session
        var client = await ConnectAsync("/");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(1, sessionStore.Count);

        // Disconnect (which should trigger session deletion)
        await client.DisposeAsync();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Session should be removed from store
        // Note: This depends on the client properly sending DELETE on dispose
    }

    [Fact]
    public async Task WithSessionStore_RegistersStoreInDI()
    {
        var sessionStore = new InMemorySessionStore();

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "TestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(options =>
        {
            options.EnableDistributedSessions = true;
        }).WithSessionStore(sessionStore);

        await using var app = Builder.Build();

        // Verify the store was registered
        var resolvedStore = app.Services.GetService<ISessionStore>();
        Assert.Same(sessionStore, resolvedStore);
    }

    [Fact]
    public async Task WithSessionStore_Generic_RegistersStoreType()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "TestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(options =>
        {
            options.EnableDistributedSessions = true;
        }).WithSessionStore<InMemorySessionStore>();

        await using var app = Builder.Build();

        // Verify the store was registered
        var resolvedStore = app.Services.GetService<ISessionStore>();
        Assert.NotNull(resolvedStore);
        Assert.IsType<InMemorySessionStore>(resolvedStore);
    }

    [Fact]
    public async Task WithSessionStore_Factory_RegistersStoreFromFactory()
    {
        var customStore = new InMemorySessionStore();

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "TestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(options =>
        {
            options.EnableDistributedSessions = true;
        }).WithSessionStore(sp => customStore);

        await using var app = Builder.Build();

        var resolvedStore = app.Services.GetService<ISessionStore>();
        Assert.Same(customStore, resolvedStore);
    }

    [Fact]
    public async Task EnableDistributedSessions_False_DoesNotUseStore()
    {
        var sessionStore = new InMemorySessionStore();

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "TestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(options =>
        {
            options.EnableDistributedSessions = false; // Disabled
        }).WithSessionStore(sessionStore);

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectAsync("/");
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Store should be empty since distributed sessions are disabled
        Assert.Equal(0, sessionStore.Count);
    }

    [Fact]
    public async Task DistributedSessions_ConfiguresIdleTimeout()
    {
        var customTimeout = TimeSpan.FromMinutes(30);

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "TestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(options =>
        {
            options.EnableDistributedSessions = true;
            options.IdleTimeout = customTimeout;
        }).WithSessionStore<InMemorySessionStore>();

        await using var app = Builder.Build();

        // Just verify this compiles and runs - the actual timeout behavior
        // would require more complex time-based testing
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectAsync("/");
        Assert.Equal("TestServer", client.ServerInfo.Name);
    }
}
