using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpStatelessTests(ITestOutputHelper outputHelper) : MapMcpStreamableHttpTests(outputHelper)
{
    protected override bool UseStreamableHttp => true;
    protected override bool Stateless => true;

    // In stateless mode each HTTP request is served by a fresh, un-negotiated McpServer, so the
    // session-level NegotiatedProtocolVersion is null when the server span is tagged. The tag must
    // therefore come from the per-request MCP-Protocol-Version header / _meta value. Exercising more
    // than one version proves the tag tracks the per-request value rather than coincidentally matching
    // a single hard-coded version (and would regress to an absent tag if the per-request fallback were
    // removed, since the negotiated value is null at tagging time).
    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ServerActivity_TagsPerRequestProtocolVersion_InStatelessMode(string protocolVersion)
    {
        var activities = new List<Activity>();
        string? capturedNegotiatedProtocolVersion = null;

        var protocolVersionTool = McpServerTool.Create(
            (RequestContext<CallToolRequestParams> context) =>
            {
                capturedNegotiatedProtocolVersion = context.Server.NegotiatedProtocolVersion;
                return "ok";
            },
            new() { Name = "stateless-capture-version" });

        using (var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource("Experimental.ModelContextProtocol")
            .AddInMemoryExporter(activities)
            .Build())
        {
            Builder.Services.AddMcpServer()
                .WithHttpTransport(ConfigureStateless)
                .WithTools([protocolVersionTool]);

            await using var app = Builder.Build();
            app.MapMcp();
            await app.StartAsync(TestContext.Current.CancellationToken);

            await using var client = await ConnectAsync(configureClient: options =>
                options.ProtocolVersion = protocolVersion);

            await client.CallToolAsync("stateless-capture-version", cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Contains(activities, activity =>
            activity.DisplayName == "tools/call stateless-capture-version" &&
            activity.Kind == ActivityKind.Server &&
            activity.GetTagItem("mcp.protocol.version") as string == protocolVersion);

        // Once the per-request version is applied, the request-scoped server settles on that same version,
        // so the value tagged on the span and the version the session ends up negotiating agree.
        Assert.Equal(protocolVersion, capturedNegotiatedProtocolVersion);
    }

    [Fact]
    public async Task EnablePollingAsync_ThrowsInvalidOperationException_InStatelessMode()
    {
        InvalidOperationException? capturedException = null;
        var pollingTool = McpServerTool.Create(async (RequestContext<CallToolRequestParams> context) =>
        {
            try
            {
                await context.EnablePollingAsync(retryInterval: TimeSpan.FromSeconds(1));
            }
            catch (InvalidOperationException ex)
            {
                capturedException = ex;
            }

            return "Complete";
        }, options: new() { Name = "polling_tool" });

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools([pollingTool]);

        await using var app = Builder.Build();
        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync();

        await mcpClient.CallToolAsync("polling_tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(capturedException);
        Assert.Contains("stateless", capturedException.Message, StringComparison.OrdinalIgnoreCase);
    }
}
