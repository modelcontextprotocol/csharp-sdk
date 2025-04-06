using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.Tests.Utils;

public class KestrelInMemoryTest : LoggedTest
{
    private readonly KestrelInMemoryTransport _inMemoryTransport = new();

    public KestrelInMemoryTest(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        Builder = WebApplication.CreateSlimBuilder();
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
