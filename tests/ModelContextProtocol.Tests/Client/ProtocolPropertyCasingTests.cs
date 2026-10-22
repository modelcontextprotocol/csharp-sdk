using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;
using System.Threading.Channels;

#pragma warning disable MCPEXP001

namespace ModelContextProtocol.Tests.Client;

public class ProtocolPropertyCasingTests
{
    [Theory]
    [InlineData("ordinary", false)]
    [InlineData("ordinary", true)]
    [InlineData("immediate-task", false)]
    [InlineData("immediate-task", true)]
    [InlineData("polled-task", false)]
    [InlineData("polled-task", true)]
    public async Task ToolResult_CanonicalFieldsWinInEitherOrder(string path, bool alternateFirst)
    {
        const string Canonical = "\"content\":[{\"type\":\"text\",\"text\":\"canonical\"}],\"isError\":false";
        const string Alternate = "\"Content\":[{\"type\":\"text\",\"text\":\"alternate\"}],\"IsError\":true";
        string resultJson = alternateFirst ? $"{{{Alternate},{Canonical}}}" : $"{{{Canonical},{Alternate}}}";
        var transport = new ControlledResponseTransport(resultJson, path == "polled-task");
        var ct = TestContext.Current.CancellationToken;
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        var request = new CallToolRequestParams { Name = "test" };

        CallToolResult result;
        if (path == "ordinary")
        {
            result = await client.CallToolAsync(request, ct);
        }
        else if (path == "immediate-task")
        {
            var augmented = await client.CallToolAsTaskAsync(request, ct);
            Assert.False(augmented.IsTask);
            result = Assert.IsType<CallToolResult>(augmented.Result);
        }
        else
        {
            result = await client.CallToolWithPollingAsync(request, cancellationToken: ct);
        }

        Assert.Equal("canonical", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.False(result.IsError);
        Assert.Equal(path == "polled-task" ? 2 : 0, transport.PollCount);
    }

    private sealed class ControlledResponseTransport(string resultJson, bool createTask) : IClientTransport, ITransport
    {
        private readonly Channel<JsonRpcMessage> _channel = Channel.CreateUnbounded<JsonRpcMessage>();

        public int PollCount { get; private set; }
        public string Name => "Controlled tool response";
        public string? SessionId => null;
        public bool IsConnected => true;
        public ChannelReader<JsonRpcMessage> MessageReader => _channel.Reader;

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult<ITransport>(this);

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            if (message is not JsonRpcRequest request)
            {
                return Task.CompletedTask;
            }

            const string TaskFields = "\"taskId\":\"test-task\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"lastUpdatedAt\":\"2026-01-01T00:00:00Z\",\"pollIntervalMs\":0";
            string json;
            switch (request.Method)
            {
                case RequestMethods.ServerDiscover:
                    json = """{"capabilities":{"tools":{}},"supportedVersions":["2026-07-28"]}""";
                    break;
                case RequestMethods.ToolsCall:
                    json = createTask ? $"{{{TaskFields},\"resultType\":\"task\",\"status\":\"working\"}}" : resultJson;
                    break;
                case "tasks/get":
                    PollCount++;
                    json = PollCount == 1
                        ? $"{{{TaskFields},\"status\":\"working\"}}"
                        : $"{{{TaskFields},\"status\":\"completed\",\"result\":{resultJson}}}";
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected request: {request.Method}");
            }

            return _channel.Writer.WriteAsync(new JsonRpcResponse { Id = request.Id, Result = JsonNode.Parse(json) }, cancellationToken).AsTask();
        }

        public ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            return default;
        }
    }
}
