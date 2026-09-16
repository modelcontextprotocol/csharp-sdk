using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

public class DockerEverythingServerTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    /// <summary>Port number to be grabbed by the next test.</summary>
    private static int s_nextPort = 3000;

    // If the tests run concurrently against different versions of the runtime, tests can conflict with
    // each other in the ports set up for interacting with containers. Ensure that such suites running
    // against different TFMs use different port numbers.
    private static readonly int s_portOffset = 1000 * (Environment.Version.Major switch
    {
        int v when v >= 8 => Environment.Version.Major - 7,
        _ => 0,
    });

    private static int CreatePortNumber() => Interlocked.Increment(ref s_nextPort) + s_portOffset;

    public static bool IsDockerAvailable => EverythingSseServerFixture.IsDockerAvailable;

    [Fact(Skip = "docker is not available", SkipUnless = nameof(IsDockerAvailable))]
    [Trait("Execution", "Manual")]
    public async Task ConnectAndReceiveMessage_EverythingServerWithSse()
    {
        int port = CreatePortNumber();

        await using var fixture = new EverythingSseServerFixture(port);
        await fixture.StartAsync();

        var defaultOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "IntegrationTestClient", Version = "1.0.0" }
        };

        var defaultConfig = new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://localhost:{port}/sse"),
            Name = "Everything",
        };

        // Create client and run tests
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(defaultConfig),
            defaultOptions, 
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotEmpty(tools);
    }

    [Fact(Skip = "docker is not available", SkipUnless = nameof(IsDockerAvailable))]
    [Trait("Execution", "Manual")]
    public async Task Sampling_Sse_EverythingServer()
    {
        int port = CreatePortNumber();

        await using var fixture = new EverythingSseServerFixture(port);
        await fixture.StartAsync();

        var defaultConfig = new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://localhost:{port}/sse"),
            Name = "Everything",
        };

        int samplingHandlerCalls = 0;
        var defaultOptions = new McpClientOptions()
        {
            Handlers = new()
            {
                SamplingHandler = async (_, _, _) =>
                {
                    samplingHandlerCalls++;
                    return new CreateMessageResult
                    {
                        Model = "test-model",
                        Role = Role.Assistant,
                        Content = [new TextContentBlock { Text = "Test response" }],
                    };
                }
            }
        };

        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(defaultConfig),
            defaultOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Call the server's trigger-sampling-request tool which should trigger our sampling handler
        var result = await client.CallToolAsync("trigger-sampling-request", new Dictionary<string, object?>
            {
                ["prompt"] = "Test prompt",
                ["maxTokens"] = 100
            }, cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        var textContent = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("text", textContent.Type);
        Assert.False(string.IsNullOrEmpty(textContent.Text));
    }
}
