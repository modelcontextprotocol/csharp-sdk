using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

public class KestrelInMemoryTest : LoggedTest
{
    private readonly KestrelInMemoryTransport _inMemoryTransport = new();

    public KestrelInMemoryTest(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        // Use SlimBuilder instead of EmptyBuilder to avoid having to call UseRouting() and UseEndpoints(_ => { })
        // or a helper that does the same every test. But clear out the existing socket transport to avoid potential port conflicts.
        Builder = WebApplication.CreateSlimBuilder();
        Builder.Services.RemoveAll<IConnectionListenerFactory>();
        Builder.Services.AddSingleton<IConnectionListenerFactory>(_inMemoryTransport);
        Builder.Services.AddSingleton(XunitLoggerProvider);

        HttpClient = new HttpClient(new SocketsHttpHandler
        {
            ConnectCallback = (context, token) =>
            {
                var connection = _inMemoryTransport.CreateConnection();
                return new(connection.ClientStream);
            },
        })
        {
            BaseAddress = new Uri("http://localhost:5000/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public WebApplicationBuilder Builder { get; }

    public HttpClient HttpClient { get; }

    public override void Dispose()
    {
        HttpClient.Dispose();
        base.Dispose();
    }
}
