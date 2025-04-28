using ModelContextProtocol.Protocol.Transport;
using System.Text;

namespace ModelContextProtocol.AspNetCore.Tests;

public class StatelessServerIntegrationTests(SseServerIntegrationTestFixture fixture, ITestOutputHelper testOutputHelper)
    : StreamableHttpServerIntegrationTests(fixture, testOutputHelper)

{
    protected override SseClientTransportOptions ClientTransportOptions => new()
    {
        Endpoint = new Uri("http://localhost/stateless"),
        Name = "TestServer",
        UseStreamableHttp = true,
    };
}
