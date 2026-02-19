using ModelContextProtocol.Client;
using System.Net;
using System.Text;

namespace ModelContextProtocol.AspNetCore.Tests;

public class SseServerIntegrationTests(SseServerIntegrationTestFixture fixture, ITestOutputHelper testOutputHelper)
    : HttpServerIntegrationTests(fixture, testOutputHelper)

{
    protected override HttpClientTransportOptions ClientTransportOptions => new()
    {
        Endpoint = new("http://localhost:5000/sse"),
        Name = "In-memory SSE Client",
    };

    [Fact]
    public async Task EventSourceResponse_Includes_ExpectedHeaders()
    {
        using var sseResponse = await _fixture.HttpClient.GetAsync("/sse", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        sseResponse.EnsureSuccessStatusCode();

        Assert.Equal("text/event-stream", sseResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("identity", sseResponse.Content.Headers.ContentEncoding.ToString());
        Assert.NotNull(sseResponse.Headers.CacheControl);
        Assert.True(sseResponse.Headers.CacheControl.NoStore);
        Assert.True(sseResponse.Headers.CacheControl.NoCache);
        Assert.Equal("no", Assert.Single(sseResponse.Headers.GetValues("X-Accel-Buffering")));
    }

    [Fact]
    public async Task EventSourceStream_Includes_MessageEventType()
    {
        // Simulate our own MCP client handshake using a plain HttpClient so we can look for "event: message"
        // in the raw SSE response stream which is not exposed by the real MCP client.
        await using var sseResponse = await _fixture.HttpClient.GetStreamAsync("/sse", TestContext.Current.CancellationToken);
        using var streamReader = new StreamReader(sseResponse);

        var endpointEvent = await streamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("event: endpoint", endpointEvent);

        var endpointData = await streamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(endpointData);
        Assert.StartsWith("data: ", endpointData);
        var messageEndpoint = endpointData["data: ".Length..];

        const string initializeRequest = """
            {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"IntegrationTestClient","version":"1.0.0"}}}
            """;
        using (var initializeRequestBody = new StringContent(initializeRequest, Encoding.UTF8, "application/json"))
        {
            using var response = await _fixture.HttpClient.PostAsync(messageEndpoint, initializeRequestBody, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        const string initializedNotification = """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """;
        using (var initializedNotificationBody = new StringContent(initializedNotification, Encoding.UTF8, "application/json"))
        {
            using var response = await _fixture.HttpClient.PostAsync(messageEndpoint, initializedNotificationBody, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.Equal("", await streamReader.ReadLineAsync(TestContext.Current.CancellationToken));
        var messageEvent = await streamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("event: message", messageEvent);
    }
}
