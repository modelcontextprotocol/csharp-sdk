using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Runtime.InteropServices;
using System.Text;

namespace ModelContextProtocol.Tests.Transport;

public class StdioClientTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    public static bool IsStdErrCallbackSupported => !PlatformDetection.IsMonoRuntime;

    [Fact]
    public async Task CreateAsync_ValidProcessInvalidServer_Throws()
    {
        string id = Guid.NewGuid().ToString("N");

        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new() { Command = "cmd", Arguments = ["/c", id] }, LoggerFactory) :
            new(new() { Command = "ls", Arguments = [id] }, LoggerFactory);

        IOException e = await Assert.ThrowsAsync<IOException>(() => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Contains(id, e.ToString());
        }
    }

    [Fact(Skip = "Platform not supported by this test.", SkipUnless = nameof(IsStdErrCallbackSupported))]
    public async Task CreateAsync_ValidProcessInvalidServer_StdErrCallbackInvoked()
    {
        string id = Guid.NewGuid().ToString("N");

        int count = 0;
        StringBuilder sb = new();
        Action<string> stdErrCallback = line =>
        {
            Assert.NotNull(line);
            lock (sb)
            {
                sb.AppendLine(line);
                count++;
            }
        };

        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new() { Command = "cmd", Arguments = ["/c", id], StandardErrorLines = stdErrCallback }, LoggerFactory) :
            new(new() { Command = "ls", Arguments = [id], StandardErrorLines = stdErrCallback }, LoggerFactory);

        await Assert.ThrowsAsync<IOException>(() => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.InRange(count, 1, int.MaxValue);
        Assert.Contains(id, sb.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("argument with spaces")]
    [InlineData("&")]
    [InlineData("|")]
    [InlineData(">")]
    [InlineData("<")]
    [InlineData("^")]
    [InlineData(" & ")]
    [InlineData(" | ")]
    [InlineData(" > ")]
    [InlineData(" < ")]
    [InlineData(" ^ ")]
    [InlineData("& ")]
    [InlineData("| ")]
    [InlineData("> ")]
    [InlineData("< ")]
    [InlineData("^ ")]
    [InlineData(" &")]
    [InlineData(" |")]
    [InlineData(" >")]
    [InlineData(" <")]
    [InlineData(" ^")]
    [InlineData("^&<>|")]
    [InlineData("^&<>| ")]
    [InlineData(" ^&<>|")]
    [InlineData("\t^&<>")]
    [InlineData("^&\t<>")]
    [InlineData("ls /tmp | grep foo.txt > /dev/null")]
    [InlineData("let rec Y f x = f (Y f) x")]
    [InlineData("value with \"quotes\" and spaces")]
    [InlineData("C:\\Program Files\\Test App\\app.dll")]
    [InlineData("C:\\EndsWithBackslash\\")]
    [InlineData("--already-looks-like-flag")]
    [InlineData("-starts-with-dash")]
    [InlineData("name=value=another")]
    [InlineData("$(echo injected)")]
    [InlineData("value-with-\"quotes\"-and-\\backslashes\\")]
    [InlineData("http://localhost:1234/callback?foo=1&bar=2")]
    public async Task EscapesCliArgumentsCorrectly(string? cliArgumentValue)
    {
        if (PlatformDetection.IsMonoRuntime && cliArgumentValue is not null && cliArgumentValue.EndsWith("\\"))
        {
            Assert.Skip("mono runtime does not handle arguments ending with backslash correctly.");
        }
        
        string cliArgument = $"--cli-arg={cliArgumentValue}";

        StdioClientTransportOptions options = new()
        {
            Name = "TestServer",
            Command = (PlatformDetection.IsMonoRuntime, PlatformDetection.IsWindows) switch
            {
                (true, _) => "mono",
                (_, true) => "TestServer.exe",
                _ => "dotnet",
            },
            Arguments = (PlatformDetection.IsMonoRuntime, PlatformDetection.IsWindows) switch
            {
                (true, _) => ["TestServer.exe", cliArgument],
                (_, true) => [cliArgument],
                _ => ["TestServer.dll", cliArgument],
            },
        };

        var transport = new StdioClientTransport(options, LoggerFactory);

        // Act: Create client (handshake) and list tools to ensure full round trip works with the argument present.
        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);

        var result = await client.CallToolAsync("echoCliArg", cancellationToken: TestContext.Current.CancellationToken);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal(cliArgumentValue ?? "", content.Text);
    }
}
