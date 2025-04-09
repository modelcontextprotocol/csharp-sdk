using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ModelContextProtocol.Tests.Utils;

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
        Builder.Services.AddSingleton(LoggerProvider);
    }

    public WebApplicationBuilder Builder { get; }

    public HttpClient CreateHttpClient()
    {
        var socketsHttpHandler = new SocketsHttpHandler()
        {
            ConnectCallback = (context, token) =>
            {
                var connection = _inMemoryTransport.CreateConnection();
                return new(connection.ClientStream);
            },
        };

        return new HttpClient(socketsHttpHandler);
    }
}
