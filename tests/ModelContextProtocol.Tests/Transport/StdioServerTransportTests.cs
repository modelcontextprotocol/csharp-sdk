using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using ModelContextProtocol.Utils.Json;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Transport;

public class StdioServerTransportTests : LoggedTest
{
    private readonly McpServerOptions _serverOptions;

    public StdioServerTransportTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "Test Server",
                Version = "1.0"
            },
            ProtocolVersion = "2.0",
            InitializationTimeout = TimeSpan.FromSeconds(10),
            ServerInstructions = "Test Instructions"
        };
    }

    [Fact(Skip="https://github.com/modelcontextprotocol/csharp-sdk/issues/143")]
    public async Task Constructor_Should_Initialize_With_Valid_Parameters()
    {
        // Act
        await using var transport = new StdioServerTransport(_serverOptions);

        // Assert
        Assert.NotNull(transport);
    }

    [Fact]
    public void Constructor_Throws_For_Null_Options()
    {
        Assert.Throws<ArgumentNullException>("serverName", () => new StdioServerTransport((string)null!));

        Assert.Throws<ArgumentNullException>("serverOptions", () => new StdioServerTransport((IOptions<McpServerOptions>)null!));
        Assert.Throws<ArgumentNullException>("serverOptions", () => new StdioServerTransport((McpServerOptions)null!));
        Assert.Throws<ArgumentNullException>("serverOptions.ServerInfo", () => new StdioServerTransport(new McpServerOptions() { ServerInfo = null! }));
        Assert.Throws<ArgumentNullException>("serverOptions.ServerInfo.Name", () => new StdioServerTransport(new McpServerOptions() { ServerInfo = new() { Name = null!, Version = "" } }));
    }

    [Fact]
    public async Task Should_Start_In_Connected_State()
    {
        await using var transport = new StreamServerTransport(new Pipe().Reader.AsStream(), Stream.Null, loggerFactory: LoggerFactory);

        Assert.True(transport.IsConnected);
    }

    [Fact]
    public async Task SendMessageAsync_Should_Send_Message()
    {
        using var output = new MemoryStream();

        await using var transport = new StreamServerTransport(
            new Pipe().Reader.AsStream(),
            output,
            loggerFactory: LoggerFactory);

        // Verify transport is connected
        Assert.True(transport.IsConnected, "Transport should be connected after StartListeningAsync");

        var message = new JsonRpcRequest { Method = "test", Id = new RequestId(44) };

        await transport.SendMessageAsync(message, TestContext.Current.CancellationToken);

        var result = Encoding.UTF8.GetString(output.ToArray()).Trim();
        var expected = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task DisposeAsync_Should_Dispose_Resources()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null, loggerFactory: LoggerFactory);

        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ReadMessagesAsync_Should_Read_Messages()
    {
        var message = new JsonRpcRequest { Method = "test", Id = new RequestId(44) };
        var json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);

        // Use a reader that won't terminate
        Pipe pipe = new();
        using var input = pipe.Reader.AsStream();

        await using var transport = new StreamServerTransport(
            input,
            Stream.Null,
            loggerFactory: LoggerFactory);

        // Verify transport is connected
        Assert.True(transport.IsConnected, "Transport should be connected after StartListeningAsync");

        // Write the message to the reader
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes($"{json}\n"), TestContext.Current.CancellationToken);

        var canRead = await transport.MessageReader.WaitToReadAsync(TestContext.Current.CancellationToken);

        Assert.True(canRead, "Nothing to read here from transport message reader");
        Assert.True(transport.MessageReader.TryPeek(out var readMessage));
        Assert.NotNull(readMessage);
        Assert.IsType<JsonRpcRequest>(readMessage);
        Assert.Equal("44", ((JsonRpcRequest)readMessage).Id.ToString());
    }

    [Fact]
    public async Task CleanupAsync_Should_Cleanup_Resources()
    {
        var transport = new StreamServerTransport(Stream.Null, Stream.Null, loggerFactory: LoggerFactory);

        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task SendMessageAsync_Should_Preserve_Unicode_Characters()
    {
        // Use a reader that won't terminate
        using var output = new MemoryStream();

        await using var transport = new StreamServerTransport(
            new Pipe().Reader.AsStream(),
            output,
            loggerFactory: LoggerFactory);

        // Verify transport is connected
        Assert.True(transport.IsConnected, "Transport should be connected after StartListeningAsync");

        // Test 1: Chinese characters (BMP Unicode)
        var chineseText = "上下文伺服器"; // "Context Server" in Chinese
        var chineseMessage = new JsonRpcRequest
        {
            Method = "test",
            Id = new RequestId(44),
            Params = new Dictionary<string, JsonElement>
            {
                ["text"] = JsonSerializer.SerializeToElement(chineseText)
            }
        };

        // Clear output and send message
        output.SetLength(0);
        await transport.SendMessageAsync(chineseMessage, TestContext.Current.CancellationToken);

        // Verify Chinese characters preserved but encoded
        var chineseResult = Encoding.UTF8.GetString(output.ToArray()).Trim();
        var expectedChinese = JsonSerializer.Serialize(chineseMessage, McpJsonUtilities.DefaultOptions);
        Assert.Equal(expectedChinese, chineseResult);
        Assert.Contains(JsonSerializer.Serialize(chineseText), chineseResult);

        // Test 2: Emoji (non-BMP Unicode using surrogate pairs)
        var emojiText = "🔍 🚀 👍"; // Magnifying glass, rocket, thumbs up
        var emojiMessage = new JsonRpcRequest
        {
            Method = "test",
            Id = new RequestId(45),
            Params = new Dictionary<string, JsonElement>
            {
                ["text"] = JsonSerializer.SerializeToElement(emojiText)
            }
        };

        // Clear output and send message
        output.SetLength(0);
        await transport.SendMessageAsync(emojiMessage, TestContext.Current.CancellationToken);

        // Verify emoji preserved - might be as either direct characters or escape sequences
        var emojiResult = Encoding.UTF8.GetString(output.ToArray()).Trim();
        var expectedEmoji = JsonSerializer.Serialize(emojiMessage, McpJsonUtilities.DefaultOptions);
        Assert.Equal(expectedEmoji, emojiResult);

        // Verify surrogate pairs in different possible formats
        // Magnifying glass emoji: 🔍 (U+1F50D)
        bool magnifyingGlassFound =
            emojiResult.Contains("🔍") ||
            emojiResult.Contains("\\ud83d\\udd0d", StringComparison.OrdinalIgnoreCase);

        // Rocket emoji: 🚀 (U+1F680)
        bool rocketFound =
            emojiResult.Contains("🚀") ||
            emojiResult.Contains("\\ud83d\\ude80", StringComparison.OrdinalIgnoreCase);

        Assert.True(magnifyingGlassFound, "Magnifying glass emoji not found in result");
        Assert.True(rocketFound, "Rocket emoji not found in result");
    }
}
