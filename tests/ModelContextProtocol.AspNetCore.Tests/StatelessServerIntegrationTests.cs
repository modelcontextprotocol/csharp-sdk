using ModelContextProtocol.Client;

namespace ModelContextProtocol.AspNetCore.Tests;

public class StatelessServerIntegrationTests(SseServerIntegrationTestFixture fixture, ITestOutputHelper testOutputHelper)
    : StreamableHttpServerIntegrationTests(fixture, testOutputHelper)
{
    protected override HttpClientTransportOptions ClientTransportOptions => new()
    {
        Endpoint = new("http://localhost:5000/stateless"),
        Name = "In-memory Streamable HTTP Client",
        TransportMode = HttpTransportMode.StreamableHttp,
    };
}
