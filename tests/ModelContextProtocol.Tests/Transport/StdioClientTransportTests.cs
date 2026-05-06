using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Transport;

public class StdioClientTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    public static bool IsStdErrCallbackSupported => !PlatformDetection.IsMonoRuntime;

    [Fact]
    public async Task CreateAsync_ValidProcessInvalidServer_Throws()
    {
        string id = Guid.NewGuid().ToString("N");

        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new() { Command = "cmd", Arguments = ["/c", $"echo {id} >&2 & exit /b 1"] }, LoggerFactory) :
            new(new() { Command = "sh", Arguments = ["-c", $"echo {id} >&2; exit 1"] }, LoggerFactory);

        await Assert.ThrowsAnyAsync<IOException>(() => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact(Skip= "Platform not supported by this test.", SkipUnless = nameof(IsStdErrCallbackSupported))]
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
            new(new() { Command = "cmd", Arguments = ["/c", $"echo {id} >&2 & exit /b 1"], StandardErrorLines = stdErrCallback }, LoggerFactory) :
            new(new() { Command = "sh", Arguments = ["-c", $"echo {id} >&2; exit 1"], StandardErrorLines = stdErrCallback }, LoggerFactory);

        await Assert.ThrowsAnyAsync<IOException>(() => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        // The stderr reading thread may not have delivered the callback yet
        // after the IOException is thrown. Poll briefly for it to arrive.
        var deadline = DateTime.UtcNow + TestConstants.DefaultTimeout;
        while (Volatile.Read(ref count) == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.InRange(count, 1, int.MaxValue);
        Assert.Contains(id, sb.ToString());
    }

    [Fact(Skip = "Platform not supported by this test.", SkipUnless = nameof(IsStdErrCallbackSupported))]
    public async Task CreateAsync_StdErrCallbackThrows_DoesNotCrashProcess()
    {
        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new() { Command = "cmd", Arguments = ["/c", "echo fail >&2 & exit /b 1"], StandardErrorLines = _ => throw new InvalidOperationException("boom") }, LoggerFactory) :
            new(new() { Command = "sh", Arguments = ["-c", "echo fail >&2; exit 1"], StandardErrorLines = _ => throw new InvalidOperationException("boom") }, LoggerFactory);

        // Should throw IOException for the failed server, not crash the host process.
        await Assert.ThrowsAnyAsync<IOException>(() => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
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
        if (PlatformDetection.IsMonoRuntime && cliArgumentValue?.EndsWith("\\") is true)
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

    [Fact(Skip = "Platform not supported by this test.", SkipUnless = nameof(IsStdErrCallbackSupported))]
    public async Task InheritEnvironmentVariables_DefaultTrue_ChildSeesParentEnvVars()
    {
        // PATH is always set in a real process environment. Verify the child sees it
        // under the default (inherit) behavior without mutating the parent process.
        var tcs = new TaskCompletionSource<string>();
        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new() { Command = "cmd", Arguments = ["/c", "if defined PATH (echo PATH_IS_SET >&2) else (echo PATH_NOT_SET >&2) & exit /b 1"], StandardErrorLines = line => tcs.TrySetResult(line) }, LoggerFactory) :
            new(new() { Command = "sh", Arguments = ["-c", "if [ -n \"$PATH\" ]; then echo PATH_IS_SET >&2; else echo PATH_NOT_SET >&2; fi; exit 1"], StandardErrorLines = line => tcs.TrySetResult(line) }, LoggerFactory);

        await Assert.ThrowsAnyAsync<IOException>(() => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        using var cts = new CancellationTokenSource(TestConstants.DefaultTimeout);
        string capturedLine = await tcs.Task.WaitAsync(cts.Token);

        Assert.Equal("PATH_IS_SET", capturedLine.Trim());
    }

    [Fact(Skip = "Platform not supported by this test.", SkipUnless = nameof(IsStdErrCallbackSupported))]
    public async Task InheritEnvironmentVariables_False_ChildDoesNotSeeParentEnvVars()
    {
        // Pass PATH so cmd/sh can be located. Verify that HOME (Unix) / USERNAME (Windows),
        // which are always set in the parent, are absent because they were not explicitly provided.
        var tcs = new TaskCompletionSource<string>();
        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new()
            {
                Command = "cmd",
                Arguments = ["/c", "if defined USERNAME (echo USERNAME_IS_SET >&2) else (echo USERNAME_NOT_SET >&2) & exit /b 1"],
                InheritEnvironmentVariables = false,
                EnvironmentVariables = new Dictionary<string, string?> { ["PATH"] = Environment.GetEnvironmentVariable("PATH") },
                StandardErrorLines = line => tcs.TrySetResult(line)
            }, LoggerFactory) :
            new(new()
            {
                Command = "sh",
                Arguments = ["-c", "if [ -n \"$HOME\" ]; then echo HOME_IS_SET >&2; else echo HOME_NOT_SET >&2; fi; exit 1"],
                InheritEnvironmentVariables = false,
                EnvironmentVariables = new Dictionary<string, string?> { ["PATH"] = Environment.GetEnvironmentVariable("PATH") },
                StandardErrorLines = line => tcs.TrySetResult(line)
            }, LoggerFactory);

        await Assert.ThrowsAnyAsync<IOException>(() => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        using var cts = new CancellationTokenSource(TestConstants.DefaultTimeout);
        string capturedLine = await tcs.Task.WaitAsync(cts.Token);

        // HOME / USERNAME were in the parent but not passed — should be absent in the child.
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "USERNAME_NOT_SET" : "HOME_NOT_SET", capturedLine.Trim());
    }

    [Fact(Skip = "Platform not supported by this test.", SkipUnless = nameof(IsStdErrCallbackSupported))]
    public async Task InheritEnvironmentVariables_False_WithExplicitVars_ChildSeesOnlyExplicitVars()
    {
        // Pass PATH + one explicit var. Verify HOME (Unix) / USERNAME (Windows) is absent,
        // and the explicitly provided variable is visible.
        const string explicitVarName = "MCP_STDIO_TEST_EXPLICIT_VAR";
        const string explicitVarValue = "explicit_test_value";

        var capturedLines = new List<string>();
        var lineCount = 0;
        var tcs = new TaskCompletionSource<bool>();
        void CaptureLines(string line)
        {
            lock (capturedLines)
            {
                capturedLines.Add(line.Trim());
                if (Interlocked.Increment(ref lineCount) >= 2)
                    tcs.TrySetResult(true);
            }
        }

        StdioClientTransport transport = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            new(new()
            {
                Command = "cmd",
                Arguments = ["/c",
                    $"if defined USERNAME (echo USERNAME_IS_SET >&2) else (echo USERNAME_NOT_SET >&2) " +
                    $"& if defined {explicitVarName} (echo EXPLICIT_IS_SET >&2) else (echo EXPLICIT_NOT_SET >&2) " +
                    $"& exit /b 1"],
                InheritEnvironmentVariables = false,
                EnvironmentVariables = new Dictionary<string, string?> { ["PATH"] = Environment.GetEnvironmentVariable("PATH"), [explicitVarName] = explicitVarValue },
                StandardErrorLines = CaptureLines
            }, LoggerFactory) :
            new(new()
            {
                Command = "sh",
                Arguments = ["-c",
                    $"if [ -n \"$HOME\" ]; then echo HOME_IS_SET >&2; else echo HOME_NOT_SET >&2; fi; " +
                    $"if [ -n \"${explicitVarName}\" ]; then echo EXPLICIT_IS_SET >&2; else echo EXPLICIT_NOT_SET >&2; fi; exit 1"],
                InheritEnvironmentVariables = false,
                EnvironmentVariables = new Dictionary<string, string?> { ["PATH"] = Environment.GetEnvironmentVariable("PATH"), [explicitVarName] = explicitVarValue },
                StandardErrorLines = CaptureLines
            }, LoggerFactory);

        await Assert.ThrowsAnyAsync<IOException>(() => McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        using var cts = new CancellationTokenSource(TestConstants.DefaultTimeout);
        await tcs.Task.WaitAsync(cts.Token);

        string allOutput = string.Join(Environment.NewLine, capturedLines);
        Assert.Contains(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "USERNAME_NOT_SET" : "HOME_NOT_SET", allOutput);
        Assert.Contains("EXPLICIT_IS_SET", allOutput);
    }

    [Fact]
    public async Task SendMessageAsync_Should_Use_LF_Not_CRLF()
    {
        using var serverInput = new MemoryStream();
        Pipe serverOutputPipe = new();

        var transport = new StreamClientTransport(serverInput, serverOutputPipe.Reader.AsStream(), LoggerFactory);
        await using var sessionTransport = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var message = new JsonRpcRequest { Method = "test", Id = new RequestId(44) };

        await sessionTransport.SendMessageAsync(message, TestContext.Current.CancellationToken);

        byte[] bytes = serverInput.ToArray();

        // The output should end with exactly \n (0x0A), not \r\n (0x0D 0x0A).
        Assert.True(bytes.Length > 1, "Output should contain message data");
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.NotEqual((byte)'\r', bytes[^2]);

        // Also verify the JSON content is valid
        var json = Encoding.UTF8.GetString(bytes).TrimEnd('\n');
        var expected = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);
        Assert.Equal(expected, json);
    }

    [Fact]
    public async Task ReadMessagesAsync_Should_Accept_CRLF_Delimited_Messages()
    {
        Pipe serverInputPipe = new();
        Pipe serverOutputPipe = new();

        var transport = new StreamClientTransport(serverInputPipe.Writer.AsStream(), serverOutputPipe.Reader.AsStream(), LoggerFactory);
        await using var sessionTransport = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var message = new JsonRpcRequest { Method = "test", Id = new RequestId(44) };
        var json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);

        // Write a \r\n-delimited message to the server's output (which the client reads)
        await serverOutputPipe.Writer.WriteAsync(Encoding.UTF8.GetBytes($"{json}\r\n"), TestContext.Current.CancellationToken);

        var canRead = await sessionTransport.MessageReader.WaitToReadAsync(TestContext.Current.CancellationToken);

        Assert.True(canRead, "Should be able to read a \\r\\n-delimited message");
        Assert.True(sessionTransport.MessageReader.TryPeek(out var readMessage));
        Assert.NotNull(readMessage);
        Assert.IsType<JsonRpcRequest>(readMessage);
        Assert.Equal("44", ((JsonRpcRequest)readMessage).Id.ToString());
    }
}
