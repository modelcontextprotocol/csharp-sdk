using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// End-to-end coverage for a default (draft-first) client connecting to a real C# Streamable HTTP
/// server that deliberately opted into sessions (<see cref="HttpServerTransportOptions.Stateless"/>
/// is <c>false</c>). Draft is sessionless (SEP-2567 / SEP-2575), so the server refuses the
/// sessionless draft probe with <c>-32004 UnsupportedProtocolVersion</c>. The client must then
/// auto-downgrade to the legacy <c>initialize</c> handshake, obtain the stateful session the server
/// author opted into, and continue to work — including a server→client elicitation round-trip
/// resolved over the stateful session via the legacy backcompat resolver.
/// </summary>
public class DraftStatefulFallbackTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    [McpServerTool(Name = "greet")]
    private static string Greet([System.ComponentModel.Description("Name to greet")] string name) => $"Hello, {name}!";

    [McpServerTool(Name = "greet_via_elicit")]
    private static async Task<string> GreetViaElicit(McpServer server, CancellationToken cancellationToken)
    {
        // Server→client round-trip: only works when the session is stateful, which is exactly what
        // the legacy fallback re-establishes for the draft-first client.
        var elicitResult = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "What is your name?",
            RequestedSchema = new(),
        }, cancellationToken);

        var name = elicitResult.Content?.TryGetValue("answer", out var answer) == true
            ? answer.GetString()
            : "stranger";

        return $"Hello, {name}!";
    }

    private async Task StartStatefulServerAsync()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = nameof(DraftStatefulFallbackTests), Version = "1" };
        })
            // Stateless = false is a deliberate opt-in to sessions. Draft can never be served
            // statefully, so the server refuses the sessionless draft probe and the client downgrades.
            .WithHttpTransport(options => options.Stateless = false)
            .WithTools([McpServerTool.Create(Greet), McpServerTool.Create(GreetViaElicit)]);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);
    }

    private async Task<McpClient> ConnectDefaultClientAsync(Action<McpClientOptions>? configureClient = null)
    {
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        // Default options: ProtocolVersion is null, which now prefers the draft revision and probes
        // with server/discover before falling back to a legacy initialize handshake.
        var clientOptions = new McpClientOptions();
        configureClient?.Invoke(clientOptions);
        return await McpClient.CreateAsync(transport, clientOptions, LoggerFactory, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DefaultDraftClient_AgainstStatefulServer_DowngradesToLegacy_AndToolsWork()
    {
        await StartStatefulServerAsync();

        await using var client = await ConnectDefaultClientAsync();

        // The sessionless draft probe was refused (-32004), so the client downgraded to legacy.
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("greet",
            new Dictionary<string, object?> { ["name"] = "Alice" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("Hello, Alice!", text);
    }

    [Fact]
    public async Task DefaultDraftClient_AgainstStatefulServer_ServerToClientElicitation_RoundTrips()
    {
        await StartStatefulServerAsync();

        await using var client = await ConnectDefaultClientAsync(options =>
        {
            options.Handlers.ElicitationHandler = (request, ct) => new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse("\"Bob\"").RootElement.Clone(),
                },
            });
        });

        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("greet_via_elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("Hello, Bob!", text);
        Assert.True(result.IsError is not true);
    }
}
