using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

public class DelegatingToolOutputSchemaTests
{
    [Theory]
    [InlineData("2025-06-18", 1, false)]
    [InlineData("2025-06-18", 2, false)]
    [InlineData("2025-06-18", 1, true)]
    [InlineData("2025-06-18", 2, true)]
    [InlineData("2025-11-25", 1, false)]
    [InlineData("2025-11-25", 2, false)]
    [InlineData("2025-11-25", 1, true)]
    [InlineData("2025-11-25", 2, true)]
    [InlineData("2026-07-28", 1, false)]
    [InlineData("2026-07-28", 2, false)]
    [InlineData("2026-07-28", 1, true)]
    [InlineData("2026-07-28", 2, true)]
    public async Task ListTools_DelegatingTool_MatchesStructuredContent(string protocolVersion, int depth, bool overrideProtocolTool)
    {
        McpServerTool inner = McpServerTool.Create(() => 72, new() { Name = "count", UseStructuredContent = true });
        McpServerTool tool = inner;
        for (int i = 0; i < depth; i++)
        {
            tool = new DelegatingTool(tool);
        }

        if (overrideProtocolTool)
        {
            tool = new DescribedTool(tool);
        }

        string originalSchema = tool.ProtocolTool.OutputSchema!.Value.GetRawText();
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;
            bool legacy = protocolVersion != "2026-07-28";

            Assert.Equal(legacy ? "object" : "integer", schema.GetProperty("type").GetString());
            if (legacy)
            {
                schema = schema.GetProperty("properties").GetProperty("result");
                Assert.Equal("integer", schema.GetProperty("type").GetString());
                Assert.Equal(72, content.GetProperty("result").GetInt32());
            }
            else
            {
                Assert.Equal(72, content.GetInt32());
            }

            if (overrideProtocolTool)
            {
                Assert.Equal("Wrapped count", listed.Description);
                Assert.Equal(0, schema.GetProperty("minimum").GetInt32());
            }
        });

        Assert.Equal(originalSchema, tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal("integer", inner.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_DelegatingCustomTool_PreservesItsOutputSchema(string protocolVersion)
    {
        McpServerTool tool = new DelegatingTool(new DelegatingTool(new CustomTool()));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("integer", listed.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString());
        });
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_DelegatingTool_HandlesOptionalOutputSchema(string protocolVersion, bool useStructuredContent)
    {
        JsonElement nullableSchema = JsonSerializer.SerializeToElement(new { type = new[] { "object", "null" } });
        McpServerTool inner = McpServerTool.Create(() => JsonSerializer.SerializeToElement(new { name = "test" }), new()
        {
            Name = "optional",
            UseStructuredContent = useStructuredContent,
            OutputSchema = useStructuredContent ? nullableSchema : null,
        });
        await WithClientAsync(new DelegatingTool(inner), protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            if (!useStructuredContent)
            {
                Assert.Null(listed.ProtocolTool.OutputSchema);
                Assert.Null(result.StructuredContent);
            }
            else
            {
                JsonElement type = listed.ProtocolTool.OutputSchema!.Value.GetProperty("type");
                if (protocolVersion == "2026-07-28")
                {
                    Assert.Equal(JsonValueKind.Array, type.ValueKind);
                }
                else
                {
                    Assert.Equal("object", type.GetString());
                }

                Assert.Equal("test", result.StructuredContent!.Value.GetProperty("name").GetString());
                Assert.Equal(JsonValueKind.Array, inner.ProtocolTool.OutputSchema!.Value.GetProperty("type").ValueKind);
            }
        });
    }

    private static async Task WithClientAsync(McpServerTool tool, string protocolVersion, Func<McpClient, Task> action)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TestConstants.DefaultTimeout);
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        await using var server = McpServer.Create(
            new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
            new McpServerOptions { ToolCollection = new() { tool } });
        Task serverTask = server.RunAsync(cts.Token);
        try
        {
            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
                new McpClientOptions { ProtocolVersion = protocolVersion }, cancellationToken: cts.Token);
            await action(client);
        }
        finally
        {
            await cts.CancelAsync();
            await serverTask;
        }
    }

    private sealed class DelegatingTool(McpServerTool inner) : DelegatingMcpServerTool(inner);

    private sealed class DescribedTool(McpServerTool inner) : DelegatingMcpServerTool(inner)
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = inner.ProtocolTool.Name,
            Description = "Wrapped count",
            InputSchema = inner.ProtocolTool.InputSchema,
            OutputSchema = JsonSerializer.SerializeToElement(new { type = "integer", minimum = 0 }),
        };
    }

    private sealed class CustomTool : McpServerTool
    {
        public override IReadOnlyList<object> Metadata => [];

        public override Tool ProtocolTool { get; } = new()
        {
            Name = "custom",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
            OutputSchema = JsonSerializer.SerializeToElement(new { type = "integer" }),
        };

        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) =>
            new(new CallToolResult { StructuredContent = JsonSerializer.SerializeToElement(72) });
    }
}
