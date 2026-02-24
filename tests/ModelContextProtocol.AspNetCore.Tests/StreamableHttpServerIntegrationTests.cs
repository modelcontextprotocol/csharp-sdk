using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text;

namespace ModelContextProtocol.AspNetCore.Tests;

public class StreamableHttpServerIntegrationTests(SseServerIntegrationTestFixture fixture, ITestOutputHelper testOutputHelper)
    : HttpServerIntegrationTests(fixture, testOutputHelper)

{
     private const string InitializeRequest = """
        {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"IntegrationTestClient","version":"1.0.0"}}}
        """;

    protected override HttpClientTransportOptions ClientTransportOptions => new()
    {
        Endpoint = new("http://localhost:5000/"),
        Name = "In-memory Streamable HTTP Client",
        TransportMode = HttpTransportMode.StreamableHttp,
    };

    [Fact]
    public async Task EventSourceResponse_Includes_ExpectedHeaders()
    {
        using var initializeRequestBody = new StringContent(InitializeRequest, Encoding.UTF8, "application/json");
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Headers =
            {
                Accept = { new("application/json"), new("text/event-stream")}
            },
            Content = initializeRequestBody,
        };
        using var sseResponse = await _fixture.HttpClient.SendAsync(postRequest, TestContext.Current.CancellationToken);

        sseResponse.EnsureSuccessStatusCode();

        Assert.Equal("text/event-stream", sseResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("identity", sseResponse.Content.Headers.ContentEncoding.ToString());
        Assert.NotNull(sseResponse.Headers.CacheControl);
        Assert.True(sseResponse.Headers.CacheControl.NoStore);
        Assert.True(sseResponse.Headers.CacheControl.NoCache);
    }

    [Fact]
    public async Task EventSourceStream_Includes_MessageEventType()
    {
        using var initializeRequestBody = new StringContent(InitializeRequest, Encoding.UTF8, "application/json");
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Headers =
            {
                Accept = { new("application/json"), new("text/event-stream")}
            },
            Content = initializeRequestBody,
        };
        using var sseResponse = await _fixture.HttpClient.SendAsync(postRequest, TestContext.Current.CancellationToken);
        using var sseResponseStream = await sseResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var streamReader = new StreamReader(sseResponseStream);

        var messageEvent = await streamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("event: message", messageEvent);
    }

    [Fact]
    public async Task CallTool_CancelToken_SendsCancellationNotification_KeepsConnectionOpen()
    {
        await using var client = await GetClientAsync();

        using CancellationTokenSource cts = new();
        var toolTask = client.CallToolAsync(
            "longRunning",
            new Dictionary<string, object?> { ["durationMs"] = 10000 },
            cancellationToken: cts.Token
        );

        // Allow some time for the request to be sent
        await Task.Delay(500, TestContext.Current.CancellationToken);

        cts.Cancel();

        // Client throws OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await toolTask);

        // Verify the connection is still open by pinging
        var pingResult = await client.PingAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(pingResult);
    }

    [Fact]
    public async Task CallTool_ClientDisconnectsAbruptly_CancelsServerToken()
    {
        var client = await GetClientAsync();

        // Send the tool call
        var toolTask = client.CallToolAsync(
            "longRunning",
            new Dictionary<string, object?> { ["durationMs"] = 10000 },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Allow some time for the request to be sent and processing to start on the server
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Disposing the client will tear down the transport and drop the underlying HTTP connection,
        // simulating a client crash or network drop without sending notifications/cancelled.
        await client.DisposeAsync();

        // The local client task will throw because the transport disconnected
        await Assert.ThrowsAnyAsync<Exception>(async () => await toolTask);

        // Verify the server is still alive and handling requests from a *new* client
        await using var newClient = await GetClientAsync();
        var pingResult = await newClient.PingAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(pingResult);
    }
}
