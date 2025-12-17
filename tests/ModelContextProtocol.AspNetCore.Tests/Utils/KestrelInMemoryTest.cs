using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

public class KestrelInMemoryTest : LoggedTest
{
    private readonly TestHttpMessageHandler _httpMessageHandler;

    public KestrelInMemoryTest(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        Builder = WebApplication.CreateEmptyBuilder(new());
        Builder.Services.AddSingleton<IConnectionListenerFactory>(KestrelInMemoryTransport);
        Builder.WebHost.UseKestrelCore();
        Builder.Services.AddRoutingCore();
        Builder.Services.AddLogging();
        Builder.Services.AddSingleton(XunitLoggerProvider);

        SocketsHttpHandler.ConnectCallback = (context, token) =>
        {
            var connection = KestrelInMemoryTransport.CreateConnection(context.DnsEndPoint);
            return new(connection.ClientStream);
        };

        _httpMessageHandler = new(SocketsHttpHandler);

        HttpClient = new HttpClient(_httpMessageHandler)
        {
            BaseAddress = new Uri("http://localhost:5000/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public WebApplicationBuilder Builder { get; }

    public HttpClient HttpClient { get; }

    public SocketsHttpHandler SocketsHttpHandler { get; } = new();

    public KestrelInMemoryTransport KestrelInMemoryTransport { get; } = new();

    protected void SetHttpMessageHandler(DelegatingHandler? handler)
    {
        if (handler is null)
        {
            _httpMessageHandler.InnerHandler = SocketsHttpHandler;
        }
        else
        {
            _httpMessageHandler.InnerHandler = handler;
            handler.InnerHandler = SocketsHttpHandler;
        }
    }

    public override void Dispose()
    {
        HttpClient.Dispose();
        base.Dispose();
    }

    private sealed class TestHttpMessageHandler : DelegatingHandler
    {
        public TestHttpMessageHandler(HttpMessageHandler innerHandler)
        {
            InnerHandler = innerHandler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => base.SendAsync(request, cancellationToken);
    }
}
