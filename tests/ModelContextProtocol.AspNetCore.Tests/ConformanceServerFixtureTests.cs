using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.ConformanceTests;
using ModelContextProtocol.Tests.Utils;
using System.Net;

namespace ModelContextProtocol.AspNetCore.Tests;

public class ConformanceServerFixtureTests(ITestOutputHelper output) : LoggedTest(output)
{
    [Fact]
    public async Task Initialize_ActualConformanceHosts_OwnDistinctReadyEndpoints()
    {
        await using var first = new ConformanceServerFixture();
        await using var second = new ConformanceServerFixture();
        await first.InitializeAsync(TestContext.Current.CancellationToken);
        await second.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(first.ServerUrl, second.ServerUrl);

        using var httpClient = new HttpClient { Timeout = TestConstants.HttpClientTimeout };
        foreach (var fixture in new[] { first, second })
        {
            var address = new Uri(fixture.ServerUrl);
            Assert.True(address.IsLoopback);
            Assert.NotEqual(0, address.Port);
            using var response = await httpClient.GetAsync($"{fixture.ServerUrl}/health", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            await using var transport = new HttpClientTransport(new() { Endpoint = new Uri(fixture.StatelessServerUrl) });
            await using var client = await TestMcpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);
            Assert.NotEmpty(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Initialize_WaitsForStartupBeforePublishingUrl()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new ConformanceServerFixture(() => CreateApplication(_ => new StartupService(async cancellationToken =>
        {
            entered.SetResult(true);
            await release.Task.WaitAsync(cancellationToken);
        })));
        var startup = fixture.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        try
        {
            await entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            Assert.False(startup.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => fixture.ServerUrl);
        }
        finally
        {
            release.TrySetResult(true);
        }
        await startup;

        using var httpClient = new HttpClient { Timeout = TestConstants.HttpClientTimeout };
        using var response = await httpClient.GetAsync($"{fixture.ServerUrl}/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Initialize_StartupFault_SurfacesOriginalError()
    {
        var expected = new InvalidOperationException("Synthetic host startup failure.");
        await using var fixture = new ConformanceServerFixture(() => CreateApplication(
            _ => new StartupService(_ => Task.FromException(expected))));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Same(expected, actual);
        Assert.Throws<InvalidOperationException>(() => fixture.ServerUrl);
    }

    [Fact]
    public async Task Initialize_HostStopsDuringStartup_DoesNotReportReady()
    {
        await using var fixture = new ConformanceServerFixture(() => CreateApplication(services =>
            new StartupService(_ =>
            {
                services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                return Task.CompletedTask;
            })));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Contains("stopped before startup completed", error.Message);
        Assert.Throws<InvalidOperationException>(() => fixture.ServerUrl);
    }

    [Fact]
    public async Task Initialize_CallerCancellation_PropagatesToHostStartup()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new ConformanceServerFixture(() => CreateApplication(_ => new StartupService(async cancellationToken =>
        {
            entered.SetResult(cancellationToken);
            try
            {
                await release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await observerRelease.Task;
                cancelled.TrySetResult(true);
                throw;
            }
        })));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var startup = fixture.InitializeAsync(cancellation.Token).AsTask();
        try
        {
            var startupToken = await entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
            Assert.True(startupToken.IsCancellationRequested);
            Assert.False(cancelled.Task.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => fixture.ServerUrl);

            var disposal = fixture.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            observerRelease.TrySetResult(true);
            Assert.True(await cancelled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
            await disposal.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            observerRelease.TrySetResult(true);
            release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task Initialize_StartupTimeout_CancelsHostStartup()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new ConformanceServerFixture(() => CreateApplication(_ => new StartupService(async cancellationToken =>
        {
            entered.SetResult(cancellationToken);
            try
            {
                await release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await observerRelease.Task;
                cancelled.TrySetResult(true);
                throw;
            }
        })));

        var startup = fixture.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        try
        {
            var startupToken = await entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<TimeoutException>(() => startup);
            Assert.Contains("30 seconds", error.Message);
            Assert.True(startupToken.IsCancellationRequested);
            Assert.False(cancelled.Task.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => fixture.ServerUrl);

            var disposal = fixture.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            observerRelease.TrySetResult(true);
            Assert.True(await cancelled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
            await disposal.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            observerRelease.TrySetResult(true);
            release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task Initialize_NoncooperativeStartup_IsBoundedAndOwnedUntilDrain()
    {
        var service = new NoncooperativeStartupService();
        var fixture = new ConformanceServerFixture(() => CreateApplication(_ => service, useNoSocketServer: true));
        var startup = fixture.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        Task? disposal = null;
        try
        {
            await service.Entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            await service.CancellationObserved.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<TimeoutException>(() =>
                startup.WaitAsync(TestConstants.HttpClientPollingTimeout, TestContext.Current.CancellationToken));
            Assert.Contains("ConformanceServer failed to start within 30 seconds", error.Message);

            disposal = fixture.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            Assert.False(service.Disposed);
            service.Release.TrySetResult(true);
            await disposal.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            Assert.True(service.Disposed);
            Assert.False(service.DisposedDuringStartup);
        }
        finally
        {
            service.Release.TrySetResult(true);
            await Assert.ThrowsAsync<TimeoutException>(() => startup);
            if (disposal is not null)
            {
                await disposal;
            }
            else
            {
                await fixture.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Dispose_NoncooperativeStartup_ReportsDeadlineAndRetainsOwnership()
    {
        var clock = new FakeTimeProvider();
        var service = new NoncooperativeStartupService();
        var fixture = new ConformanceServerFixture(
            () => CreateApplication(_ => service, useNoSocketServer: true), clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var startup = fixture.InitializeAsync(cancellation.Token).AsTask();
        try
        {
            await service.Entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                startup.WaitAsync(TestConstants.HttpClientPollingTimeout, TestContext.Current.CancellationToken));

            var disposal = fixture.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            clock.Advance(TestConstants.DefaultTimeout);
            var error = await Assert.ThrowsAsync<TimeoutException>(() =>
                disposal.WaitAsync(TestConstants.HttpClientPollingTimeout, TestContext.Current.CancellationToken));
            Assert.Contains("cleanup did not complete within 60 seconds", error.Message);
            Assert.False(service.Disposed);

            service.Release.TrySetResult(true);
            await fixture.DisposeAsync();
            Assert.True(service.Disposed);
            Assert.False(service.DisposedDuringStartup);
        }
        finally
        {
            service.Release.TrySetResult(true);
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Dispose_LateStartupFault_IsReportedAfterSafeCleanup()
    {
        var expected = new InvalidOperationException("Synthetic late startup failure.");
        var service = new NoncooperativeStartupService { Failure = expected };
        var fixture = new ConformanceServerFixture(() => CreateApplication(_ => service, useNoSocketServer: true));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var startup = fixture.InitializeAsync(cancellation.Token).AsTask();
        try
        {
            await service.Entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                startup.WaitAsync(TestConstants.HttpClientPollingTimeout, TestContext.Current.CancellationToken));

            var disposal = fixture.DisposeAsync().AsTask();
            Assert.False(service.Disposed);
            service.Release.TrySetResult(true);
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => disposal);
            Assert.Same(expected, actual);
            Assert.True(service.Disposed);
            Assert.False(service.DisposedDuringStartup);
        }
        finally
        {
            service.Release.TrySetResult(true);
            await fixture.DisposeAsync();
        }
    }

    private WebApplication CreateApplication(Func<IServiceProvider, IHostedService> serviceFactory, bool useNoSocketServer = false)
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0"]);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(XunitLoggerProvider);
        if (useNoSocketServer)
        {
            builder.Services.AddSingleton<IServer, NoSocketServer>();
        }
        builder.Services.AddSingleton(serviceFactory);
        var app = builder.Build();
        app.MapGet("/health", () => "Healthy");
        return app;
    }

    private sealed class StartupService(Func<CancellationToken, Task> start) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => start(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoncooperativeStartupService : IHostedService, IDisposable
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool StartupCompleted { get; private set; }
        public bool Disposed { get; private set; }
        public bool DisposedDuringStartup { get; private set; }
        public Exception? Failure { get; init; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => CancellationObserved.TrySetResult(true));
            Entered.TrySetResult(true);
            await Release.Task;
            StartupCompleted = true;
            if (Failure is { } error)
            {
                throw error;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
            DisposedDuringStartup = !StartupCompleted;
            Disposed = true;
        }
    }

    private sealed class NoSocketServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();
        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
