using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Wire-format conformance tests for <see cref="McpServer"/> driven directly against the underlying
/// stream — without going through <see cref="ModelContextProtocol.Client.McpClient"/>. This exercises the
/// SEP-2575 (sessionless / no-initialize) and SEP-2567 (server/discover) flows by hand-crafting JSON-RPC
/// messages and asserting on the exact responses the server emits.
/// </summary>
/// <remarks>
/// The tests use a paired <see cref="Pipe"/> the way <see cref="ClientServerTestBase"/> does, but instead
/// of constructing an <c>McpClient</c> we read and write JSON-RPC envelopes directly. This is the closest
/// approximation we have to a third-party / non-SDK client and is what conformance tooling will exercise.
/// </remarks>
public sealed class RawStreamConformanceTests : LoggedTest, IAsyncDisposable
{
    private const string DraftVersion = McpHttpHeaders.DraftProtocolVersion;

    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly Task _serverTask;
    private readonly ServiceProvider _services;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    public RawStreamConformanceTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(XunitLoggerProvider));
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "raw-conformance-server", Version = "1.0.0" };
            })
            .WithStreamServerTransport(_clientToServer.Reader.AsStream(), _serverToClient.Writer.AsStream())
            .WithTools([
                McpServerTool.Create((string text) => $"echo:{text}", new() { Name = "echo" }),
            ]);

        _services = services.BuildServiceProvider(validateScopes: true);
        var server = _services.GetRequiredService<McpServer>();
        _serverTask = server.RunAsync(_cts.Token);

        _writer = new StreamWriter(_clientToServer.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        _reader = new StreamReader(_serverToClient.Reader.AsStream(), Encoding.UTF8);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _clientToServer.Writer.Complete();
        _serverToClient.Writer.Complete();
        try { await _serverTask; } catch { /* expected on cancellation */ }
        await _services.DisposeAsync();
        _cts.Dispose();
        Dispose();
    }

    private async Task SendAsync(string json) => await _writer.WriteLineAsync(json);

    private async Task<JsonNode> ReadAsync()
    {
        var line = await _reader.ReadLineAsync(_cts.Token);
        Assert.NotNull(line);
        return JsonNode.Parse(line!)!;
    }

    private static string DraftMetaFragment(string protocolVersion = DraftVersion) =>
        @"""_meta"":{""io.modelcontextprotocol/protocolVersion"":""" + protocolVersion +
        @""",""io.modelcontextprotocol/clientInfo"":{""name"":""raw"",""version"":""1.0""}," +
        @"""io.modelcontextprotocol/clientCapabilities"":{}}";

    [Fact]
    public async Task ServerDiscover_ReturnsSupportedVersionsIncludingDraft()
    {
        await SendAsync(@"{""jsonrpc"":""2.0"",""id"":1,""method"":""server/discover"",""params"":{" + DraftMetaFragment() + "}}");

        var response = await ReadAsync();
        Assert.Equal("2.0", response["jsonrpc"]!.GetValue<string>());
        Assert.Equal(1, response["id"]!.GetValue<int>());

        var result = response["result"];
        Assert.NotNull(result);

        var supportedVersions = result!["supportedVersions"]!.AsArray()
            .Select(n => n!.GetValue<string>())
            .ToList();
        Assert.Contains(DraftVersion, supportedVersions);

        // Capabilities and serverInfo are mandatory in DiscoverResult per SEP-2575.
        Assert.NotNull(result["capabilities"]);
        Assert.NotNull(result["serverInfo"]);
        Assert.Equal("raw-conformance-server", result["serverInfo"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task DraftToolsCall_WithoutInitialize_Succeeds_WhenFullMetaProvided()
    {
        // Spec: under SEP-2575 the client may skip server/discover and go straight to a normal RPC, as long
        // as every request carries the full _meta envelope with protocolVersion, clientInfo and capabilities.
        await SendAsync(
            @"{""jsonrpc"":""2.0"",""id"":42,""method"":""tools/call"",""params"":{""name"":""echo"",""arguments"":{""text"":""hello""}," +
            DraftMetaFragment() + "}}");

        var response = await ReadAsync();
        Assert.Equal(42, response["id"]!.GetValue<int>());
        var result = response["result"];
        Assert.NotNull(result);
        var content = result!["content"]!.AsArray();
        Assert.Single(content);
        Assert.Equal("echo:hello", content[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task DraftRequest_WithUnsupportedProtocolVersion_ReturnsMinus32004WithSupported()
    {
        // Server should respond with UnsupportedProtocolVersionError (-32004) and a data.supported[] list.
        await SendAsync(
            @"{""jsonrpc"":""2.0"",""id"":7,""method"":""tools/call"",""params"":{""name"":""echo"",""arguments"":{""text"":""x""}," +
            DraftMetaFragment("9999-99-99") + "}}");

        var response = await ReadAsync();
        Assert.Equal(7, response["id"]!.GetValue<int>());
        var error = response["error"];
        Assert.NotNull(error);
        Assert.Equal((int)McpErrorCode.UnsupportedProtocolVersion, error!["code"]!.GetValue<int>());

        var data = error["data"];
        Assert.NotNull(data);
        Assert.Equal("9999-99-99", data!["requested"]!.GetValue<string>());
        var supported = data["supported"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains(DraftVersion, supported);
    }

    [Fact]
    public async Task LegacyInitialize_StillWorks_OnDraftDefaultServer()
    {
        // Dual-era: a draft-default server (ProtocolVersion = DraftVersion in McpServerOptions) must still
        // accept the legacy initialize handshake from clients that don't speak the new protocol.
        await SendAsync(@"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{""protocolVersion"":""2025-11-25"",""capabilities"":{},""clientInfo"":{""name"":""legacy"",""version"":""1.0""}}}");

        var response = await ReadAsync();
        Assert.Equal(1, response["id"]!.GetValue<int>());
        var result = response["result"];
        Assert.NotNull(result);
        Assert.Equal("2025-11-25", result!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task MixedSequence_Discover_Then_Initialize_Then_ToolsCall_AllSucceed()
    {
        // Dual-era servers must accept draft and legacy traffic on the same connection. The exact mix below
        // is what a permissive client running against an unknown server would emit while probing.
        await SendAsync(@"{""jsonrpc"":""2.0"",""id"":1,""method"":""server/discover"",""params"":{" + DraftMetaFragment() + "}}");
        var discover = await ReadAsync();
        Assert.NotNull(discover["result"]);

        await SendAsync(@"{""jsonrpc"":""2.0"",""id"":2,""method"":""initialize"",""params"":{""protocolVersion"":""2025-11-25"",""capabilities"":{},""clientInfo"":{""name"":""legacy"",""version"":""1.0""}}}");
        var init = await ReadAsync();
        Assert.NotNull(init["result"]);
        Assert.Equal("2025-11-25", init["result"]!["protocolVersion"]!.GetValue<string>());

        await SendAsync(@"{""jsonrpc"":""2.0"",""method"":""notifications/initialized"",""params"":{}}");

        await SendAsync(@"{""jsonrpc"":""2.0"",""id"":3,""method"":""tools/call"",""params"":{""name"":""echo"",""arguments"":{""text"":""after-init""}}}");
        var call = await ReadAsync();
        Assert.Equal("echo:after-init", call["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }
}
