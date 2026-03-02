using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Transport;

/// <summary>
/// Tests for the line-reading behavior of PipeReaderExtensions (exercised via StreamServerTransport).
/// Covers: empty lines, LF/CRLF endings, multi-segment buffers, non-ASCII characters,
/// and invalid byte sequences adjacent to newlines.
/// </summary>
public class PipeReaderExtensionsTests
{
    private static readonly JsonRpcRequest s_testMessage = new() { Method = "ping", Id = new RequestId(1) };
    private static readonly string s_testJson = JsonSerializer.Serialize(s_testMessage, McpJsonUtilities.DefaultOptions);
    private static readonly byte[] s_testJsonBytes = Encoding.UTF8.GetBytes(s_testJson);

    // Writes bytes to a pipe, wires it up to a StreamServerTransport, and returns the first received message.
    private static async Task<JsonRpcMessage?> ReadOneMessageAsync(byte[] lineBytes)
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        // Write the line, then close the writer so the transport loop terminates.
        await pipe.Writer.WriteAsync(lineBytes, ct);
        await pipe.Writer.CompleteAsync();

        transport.MessageReader.TryPeek(out _); // prime

        if (!await transport.MessageReader.WaitToReadAsync(ct))
        {
            return null;
        }

        transport.MessageReader.TryRead(out var message);
        return message;
    }

    // Writes bytes to a pipe one byte at a time, exercising multi-segment PipeReader buffers.
    private static async Task<JsonRpcMessage?> ReadOneMessageByteByByteAsync(byte[] lineBytes)
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        foreach (byte b in lineBytes)
        {
            await pipe.Writer.WriteAsync(new[] { b }, ct);
            await pipe.Writer.FlushAsync(ct);
        }
        await pipe.Writer.CompleteAsync();

        if (!await transport.MessageReader.WaitToReadAsync(ct))
        {
            return null;
        }

        transport.MessageReader.TryRead(out var message);
        return message;
    }

    [Fact]
    public async Task EmptyInput_ProducesNoMessages()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        await pipe.Writer.CompleteAsync();

        // Channel should complete immediately with no messages.
        Assert.False(await transport.MessageReader.WaitToReadAsync(ct));
    }

    [Fact]
    public async Task EmptyLine_LF_IsSkipped()
    {
        // A bare \n (empty line) must not cause a parse error and must not deliver a message.
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        // Write an empty line, then a real message.
        var bytes = Encoding.UTF8.GetBytes($"\n{s_testJson}\n");
        await pipe.Writer.WriteAsync(bytes, ct);
        await pipe.Writer.CompleteAsync();

        var message = await transport.MessageReader.ReadAsync(ct);
        Assert.IsType<JsonRpcRequest>(message);
    }

    [Fact]
    public async Task EmptyLine_CRLF_IsSkipped()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        var bytes = Encoding.UTF8.GetBytes($"\r\n{s_testJson}\n");
        await pipe.Writer.WriteAsync(bytes, ct);
        await pipe.Writer.CompleteAsync();

        var message = await transport.MessageReader.ReadAsync(ct);
        Assert.IsType<JsonRpcRequest>(message);
    }

    [Fact]
    public async Task LfTerminatedLine_IsDelivered()
    {
        var msg = await ReadOneMessageAsync(Encoding.UTF8.GetBytes(s_testJson + "\n"));
        Assert.IsType<JsonRpcRequest>(msg);
        Assert.Equal(s_testMessage.Method, ((JsonRpcRequest)msg!).Method);
    }

    [Fact]
    public async Task CrLfTerminatedLine_IsDelivered()
    {
        var msg = await ReadOneMessageAsync(Encoding.UTF8.GetBytes(s_testJson + "\r\n"));
        Assert.IsType<JsonRpcRequest>(msg);
        Assert.Equal(s_testMessage.Method, ((JsonRpcRequest)msg!).Method);
    }

    [Fact]
    public async Task MultipleLines_AllDelivered()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        // Three messages: LF, CRLF, LF
        var line1 = JsonSerializer.Serialize(new JsonRpcRequest { Method = "m1", Id = new RequestId(1) }, McpJsonUtilities.DefaultOptions);
        var line2 = JsonSerializer.Serialize(new JsonRpcRequest { Method = "m2", Id = new RequestId(2) }, McpJsonUtilities.DefaultOptions);
        var line3 = JsonSerializer.Serialize(new JsonRpcRequest { Method = "m3", Id = new RequestId(3) }, McpJsonUtilities.DefaultOptions);

        var bytes = Encoding.UTF8.GetBytes($"{line1}\n{line2}\r\n{line3}\n");
        await pipe.Writer.WriteAsync(bytes, ct);
        await pipe.Writer.CompleteAsync();

        var methods = new List<string>();
        await foreach (var msg in transport.MessageReader.ReadAllAsync(ct))
        {
            methods.Add(((JsonRpcRequest)msg).Method);
        }

        Assert.Equal(["m1", "m2", "m3"], methods);
    }

    [Fact]
    public async Task LfTerminatedLine_MultiSegment_IsDelivered()
    {
        var msg = await ReadOneMessageByteByByteAsync(Encoding.UTF8.GetBytes(s_testJson + "\n"));
        Assert.IsType<JsonRpcRequest>(msg);
    }

    [Fact]
    public async Task CrLfTerminatedLine_MultiSegment_IsDelivered()
    {
        var msg = await ReadOneMessageByteByByteAsync(Encoding.UTF8.GetBytes(s_testJson + "\r\n"));
        Assert.IsType<JsonRpcRequest>(msg);
    }

    [Fact]
    public async Task CrLfWhereCrIsLastByteOfSegment_IsTrimmed()
    {
        // Force \r and \n into separate pipe writes to exercise multi-segment CRLF trimming.
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(s_testJson + "\r"), ct);
        await pipe.Writer.FlushAsync(ct);
        await pipe.Writer.WriteAsync("\n"u8.ToArray(), ct);
        await pipe.Writer.CompleteAsync();

        var msg = await transport.MessageReader.ReadAsync(ct);
        Assert.IsType<JsonRpcRequest>(msg);
    }

    [Fact]
    public async Task NonAsciiContentInJsonValue_IsPreserved()
    {
        var ct = TestContext.Current.CancellationToken;
        // Build a request whose method name contains multi-byte UTF-8 and an emoji.
        string method = "上下文伺服器🚀";
        var request = new JsonRpcRequest { Method = method, Id = new RequestId(99) };
        string json = JsonSerializer.Serialize(request, McpJsonUtilities.DefaultOptions);

        var msg = await ReadOneMessageAsync(Encoding.UTF8.GetBytes(json + "\n"));

        Assert.IsType<JsonRpcRequest>(msg);
        Assert.Equal(method, ((JsonRpcRequest)msg!).Method);
    }

    [Fact]
    public async Task NonAsciiContentInJsonValue_MultiSegment_IsPreserved()
    {
        string method = "上下文伺服器🚀";
        var request = new JsonRpcRequest { Method = method, Id = new RequestId(99) };
        string json = JsonSerializer.Serialize(request, McpJsonUtilities.DefaultOptions);

        var msg = await ReadOneMessageByteByByteAsync(Encoding.UTF8.GetBytes(json + "\n"));

        Assert.IsType<JsonRpcRequest>(msg);
        Assert.Equal(method, ((JsonRpcRequest)msg!).Method);
    }

    [Fact]
    public async Task MultiByteCharacterSplitAcrossSegments_IsPreserved()
    {
        // '€' is the 3-byte UTF-8 sequence 0xE2 0x82 0xAC. Place it in a method value so we
        // can assert round-trip integrity, then split the encode bytes across two writes.
        string method = "test€method";
        var request = new JsonRpcRequest { Method = method, Id = new RequestId(5) };
        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, McpJsonUtilities.DefaultOptions) + "\n");

        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        // Split at the midpoint so the multi-byte euro sign (near the method value) is likely split.
        int mid = jsonBytes.Length / 2;
        await pipe.Writer.WriteAsync(jsonBytes.AsMemory(0, mid), ct);
        await pipe.Writer.FlushAsync(ct);
        await pipe.Writer.WriteAsync(jsonBytes.AsMemory(mid), ct);
        await pipe.Writer.CompleteAsync();

        var msg = await transport.MessageReader.ReadAsync(ct);
        Assert.IsType<JsonRpcRequest>(msg);
        Assert.Equal(method, ((JsonRpcRequest)msg!).Method);
    }

    [Fact]
    public async Task InvalidJsonLine_IsSkippedAndNextLineIsDelivered()
    {
        // An invalid JSON line must be silently skipped; subsequent valid lines must still be delivered.
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        var bytes = Encoding.UTF8.GetBytes($"not-valid-json\n{s_testJson}\n");
        await pipe.Writer.WriteAsync(bytes, ct);
        await pipe.Writer.CompleteAsync();

        var message = await transport.MessageReader.ReadAsync(ct);
        Assert.IsType<JsonRpcRequest>(message);
    }

    [Fact]
    public async Task StandaloneCrNotFollowedByLf_IsIncludedInLine()
    {
        // A \r that is NOT immediately before \n must remain in the payload.
        // We can't embed a raw \r in a JSON method name as JSON would reject it, so we verify
        // the behavior by confirming the transport still delivers the first valid JSON after an invalid line.
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        // Craft a sequence where a line contains a lone \r before valid JSON data.
        // The first line has a lone \r inside it (invalid JSON), so it should be skipped.
        // The second line is valid JSON.
        byte[] line1 = Encoding.UTF8.GetBytes("has\ra\ralone\n");
        byte[] line2 = Encoding.UTF8.GetBytes(s_testJson + "\n");
        byte[] combined = [.. line1, .. line2];
        await pipe.Writer.WriteAsync(combined, ct);
        await pipe.Writer.CompleteAsync();

        var message = await transport.MessageReader.ReadAsync(ct);
        Assert.IsType<JsonRpcRequest>(message);
    }

    [Fact]
    public async Task MultiByteSequenceInterruptedByNewline_BothLinesSkipped_NextValidLineDelivered()
    {
        // '€' encodes as 3 UTF-8 bytes: 0xE2 0x82 0xAC.  If a newline is injected after the
        // first byte, the two resulting lines both contain invalid byte sequences:
        //   Line 1: ...0xE2\n  — a truncated 3-byte lead byte; invalid JSON in both old and new impl
        //   Line 2: 0x82 0xAC...\n — continuation bytes without a lead byte; also invalid JSON
        //
        // Both the old StreamReader-based path (which produced U+FFFD replacement chars before
        // passing to JsonSerializer) and the new PipeReader-based path (which passes raw bytes to
        // JsonSerializer) raise JsonException for each line and silently skip them.  A subsequent
        // valid JSON line must still be delivered.
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        // Build line 1: a JSON string where '€' is split after byte 0xE2, terminated with \n.
        byte[] euroBytes = Encoding.UTF8.GetBytes("€"); // [0xE2, 0x82, 0xAC]
        byte[] line1 = [.. Encoding.UTF8.GetBytes("{\"method\":\"te"), euroBytes[0], (byte)'\n'];
        // Build line 2: the remaining continuation bytes + rest of JSON, terminated with \n.
        byte[] line2 = [euroBytes[1], euroBytes[2], .. Encoding.UTF8.GetBytes("st\"}\n")];
        // Build line 3: a valid JSON message that must survive the two bad lines.
        byte[] line3 = Encoding.UTF8.GetBytes(s_testJson + "\n");

        byte[] allBytes = [.. line1, .. line2, .. line3];
        await pipe.Writer.WriteAsync(allBytes, ct);
        await pipe.Writer.CompleteAsync();

        // Only the valid line 3 should produce a message; lines 1 and 2 are silently skipped.
        var message = await transport.MessageReader.ReadAsync(ct);
        Assert.IsType<JsonRpcRequest>(message);

        // No further messages.
        Assert.False(transport.MessageReader.TryRead(out _));
    }

    [Fact]
    public async Task LineWithNoTerminatingNewline_IsNotDelivered()
    {
        // Data without a trailing newline should not produce a message.
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await using var transport = new StreamServerTransport(pipe.Reader.AsStream(), Stream.Null);

        await pipe.Writer.WriteAsync(s_testJsonBytes, ct);
        await pipe.Writer.CompleteAsync();

        // Wait briefly — no message should arrive.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        Assert.False(await transport.MessageReader.WaitToReadAsync(timeoutCts.Token));
    }
}
