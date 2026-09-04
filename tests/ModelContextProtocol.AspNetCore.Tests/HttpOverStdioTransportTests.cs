#pragma warning disable MCPEXP001 // HTTP over stdio is experimental.

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using System.IO.Pipelines;
using System.Net;
using System.Reflection;

namespace ModelContextProtocol.AspNetCore.Tests;

public class HttpOverStdioTransportTests
{
    [Fact]
    public void WithHttpOverStdioTransport_RegistersInfrastructureIdempotently()
    {
        ServiceCollection services = new();

        IMcpServerBuilder builder = services.AddMcpServer();
        Assert.Same(builder, builder.WithHttpOverStdioTransport());
        builder.WithHttpOverStdioTransport();

        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IConnectionListenerFactory) &&
            descriptor.ImplementationType?.Name == "StdioConnectionListenerFactory");
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<KestrelServerOptions>) &&
            descriptor.ImplementationType?.Name == "HttpOverStdioKestrelOptionsSetup");
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<ConsoleLoggerOptions>) &&
            descriptor.ImplementationType?.Name == "HttpOverStdioConsoleLoggerOptionsSetup");

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        Assert.Equal(
            LogLevel.Trace,
            serviceProvider.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.LogToStandardErrorThreshold);
    }

    [Fact]
    public void WithHttpOverStdioTransport_PreservesHttpTransportOptions()
    {
        ServiceCollection services = new();

        services.AddMcpServer().WithHttpOverStdioTransport(options =>
        {
            options.SessionMode = HttpServerSessionMode.Stateful;
        });

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        HttpServerTransportOptions options =
            serviceProvider.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;

        Assert.Equal(HttpServerSessionMode.Stateful, options.SessionMode);
    }

    [Fact]
    public void KestrelOptions_ContainOnlyHttp2StdioListenerByDefault()
    {
        ServiceCollection services = new();
        services.AddMcpServer().WithHttpOverStdioTransport();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        KestrelServerOptions options =
            serviceProvider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        ListenOptions listener = Assert.Single(GetCodeBackedListenOptions(options));
        Assert.Same(GetStdioEndPoint(), listener.EndPoint);
        Assert.Equal(HttpProtocols.Http2, listener.Protocols);
    }

    [Fact]
    public void KestrelOptions_PreserveExplicitTcpListener()
    {
        ServiceCollection services = new();
        services.Configure<KestrelServerOptions>(options => options.Listen(IPAddress.Loopback, 0));
        services.AddMcpServer().WithHttpOverStdioTransport();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        KestrelServerOptions options =
            serviceProvider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.Collection(
            GetCodeBackedListenOptions(options),
            listener => Assert.IsType<IPEndPoint>(listener.EndPoint),
            listener =>
            {
                Assert.Same(GetStdioEndPoint(), listener.EndPoint);
                Assert.Equal(HttpProtocols.Http2, listener.Protocols);
            });
    }

    [Fact]
    public void ListenerFactorySelector_OnlyAcceptsSingletonStdioEndpoint()
    {
        IConnectionListenerFactory factory =
            CreateListenerFactory(new TestHostApplicationLifetime(), new MemoryStream(), new MemoryStream());
        IConnectionListenerFactorySelector selector =
            Assert.IsAssignableFrom<IConnectionListenerFactorySelector>(factory);

        Assert.True(selector.CanBind(GetStdioEndPoint()));
        Assert.False(selector.CanBind(new IPEndPoint(IPAddress.Loopback, 0)));
        Assert.False(selector.CanBind(new DnsEndPoint("localhost", 0)));
    }

    [Fact]
    public async Task Listener_AcceptsOneConnection_ThenWaitsForCancellationOrUnbind()
    {
        IConnectionListenerFactory factory =
            CreateListenerFactory(new TestHostApplicationLifetime(), new MemoryStream(), new MemoryStream());
        await using IConnectionListener listener =
            await factory.BindAsync(GetStdioEndPoint(), TestContext.Current.CancellationToken);

        ConnectionContext? connection =
            await listener.AcceptAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(connection);

        using (CancellationTokenSource cancellationSource = new())
        {
            Task<ConnectionContext?> canceledAccept = listener.AcceptAsync(cancellationSource.Token).AsTask();
            Assert.False(canceledAccept.IsCompleted);

            cancellationSource.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledAccept);
        }

        Task<ConnectionContext?> unboundAccept =
            listener.AcceptAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.False(unboundAccept.IsCompleted);

        await listener.UnbindAsync(TestContext.Current.CancellationToken);
        Assert.Null(await unboundAccept);
        Assert.Null(await listener.AcceptAsync(TestContext.Current.CancellationToken));

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task ConnectionCompletion_StopsApplication_AndLeavesStreamsOpen()
    {
        TestHostApplicationLifetime lifetime = new();
        TrackingMemoryStream input = new([]);
        TrackingMemoryStream output = new();
        ConnectionContext connection = CreateConnectionContext(input, output, lifetime);

        ReadResult result = await connection.Transport.Input.ReadAsync(TestContext.Current.CancellationToken);
        connection.Transport.Input.AdvanceTo(result.Buffer.End);

        Assert.True(result.IsCompleted);
        Assert.Equal(1, lifetime.StopApplicationCallCount);

        await connection.DisposeAsync();

        Assert.Equal(1, lifetime.StopApplicationCallCount);
        Assert.False(input.IsDisposed);
        Assert.False(output.IsDisposed);
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        public int StopApplicationCallCount { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            StopApplicationCallCount++;
        }
    }

    private static IReadOnlyList<ListenOptions> GetCodeBackedListenOptions(KestrelServerOptions options)
    {
        PropertyInfo property = typeof(KestrelServerOptions).GetProperty(
            "CodeBackedListenOptions",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (IReadOnlyList<ListenOptions>)property.GetValue(options)!;
    }

    private static EndPoint GetStdioEndPoint()
    {
        Type endpointType = typeof(HttpMcpServerBuilderExtensions).Assembly.GetType(
            "ModelContextProtocol.AspNetCore.StdioEndPoint",
            throwOnError: true)!;
        return (EndPoint)endpointType.GetProperty(
            "Instance",
            BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    }

    private static IConnectionListenerFactory CreateListenerFactory(
        IHostApplicationLifetime applicationLifetime,
        Stream input,
        Stream output)
    {
        Type factoryType = typeof(HttpMcpServerBuilderExtensions).Assembly.GetType(
            "ModelContextProtocol.AspNetCore.StdioConnectionListenerFactory",
            throwOnError: true)!;
        return (IConnectionListenerFactory)Activator.CreateInstance(
            factoryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [applicationLifetime, input, output],
            culture: null)!;
    }

    private static ConnectionContext CreateConnectionContext(
        Stream input,
        Stream output,
        IHostApplicationLifetime applicationLifetime)
    {
        Type connectionType = typeof(HttpMcpServerBuilderExtensions).Assembly.GetType(
            "ModelContextProtocol.AspNetCore.StdioConnectionContext",
            throwOnError: true)!;
        return (ConnectionContext)Activator.CreateInstance(
            connectionType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [input, output, applicationLifetime],
            culture: null)!;
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public TrackingMemoryStream()
        {
        }

        public TrackingMemoryStream(byte[] buffer)
            : base(buffer)
        {
        }

        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
