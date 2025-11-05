using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public class LargeMessageTests
{
    /// <summary>
    /// Tests that messages of various sizes are sent completely without truncation or hanging.
    /// This verifies the fix for the large message hanging issue where messages >16KB would hang
    /// due to buffer deadlocks when JSON and newline were written in a single operation.
    /// </summary>
    [Theory]
    [InlineData(15 * 1024)]  // 15KB - just below the typical buffer size
    [InlineData(16 * 1024)]  // 16KB - at the typical buffer size boundary
    [InlineData(17 * 1024)]  // 17KB - just above the typical buffer size
    [InlineData(32 * 1024)]  // 32KB - double the buffer size
    [InlineData(100 * 1024)] // 100KB - significantly larger message
    public async Task SendMessageAsync_LargeMessage_DeliversCompleteMessageWithNewline(int messageSize)
    {
        // Arrange - Create a memory stream to capture output
        using var outputStream = new MemoryStream();
        using var inputStream = new MemoryStream();
        
        await using var transport = new StreamServerTransport(inputStream, outputStream);
        
        // Create a message with content approximately the target size
        string largeContent = new string('X', messageSize);
        var callToolResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = largeContent }]
        };
        
        var message = new JsonRpcResponse
        {
            Id = new RequestId(1),
            Result = JsonSerializer.SerializeToNode(callToolResult, McpJsonUtilities.DefaultOptions)
        };
        
        // Act - Send the message
        await transport.SendMessageAsync(message, CancellationToken.None);
        
        // Assert - Verify complete message was written
        outputStream.Position = 0;
        byte[] writtenBytes = outputStream.ToArray();
        
        // Verify the message ends with a newline
        Assert.True(writtenBytes.Length > 0, "No data was written to the output stream");
        Assert.Equal((byte)'\n', writtenBytes[^1]);
        
        // Verify the message is valid JSON (excluding the trailing newline)
        string messageJson = Encoding.UTF8.GetString(writtenBytes, 0, writtenBytes.Length - 1);
        var deserializedMessage = JsonSerializer.Deserialize<JsonRpcResponse>(
            messageJson, 
            McpJsonUtilities.DefaultOptions);
        
        // Verify the deserialized message matches the original
        Assert.NotNull(deserializedMessage);
        Assert.Equal(message.Id, deserializedMessage.Id);
        
        // Verify the content was not truncated
        var resultContent = deserializedMessage.Result?.Deserialize<CallToolResult>(McpJsonUtilities.DefaultOptions);
        Assert.NotNull(resultContent);
        Assert.NotNull(resultContent.Content);
        Assert.Single(resultContent.Content);
        
        var textContent = resultContent.Content[0] as TextContentBlock;
        Assert.NotNull(textContent);
        Assert.Equal(largeContent, textContent.Text);
    }

    /// <summary>
    /// Tests that multiple large messages can be sent sequentially without issues.
    /// </summary>
    [Fact]
    public async Task SendMessageAsync_MultipleLargeMessages_AllDeliveredSuccessfully()
    {
        // Arrange
        using var outputStream = new MemoryStream();
        using var inputStream = new MemoryStream();
        
        await using var transport = new StreamServerTransport(inputStream, outputStream);
        
        var messageSizes = new[] { 20 * 1024, 50 * 1024, 30 * 1024 };
        var messages = new List<JsonRpcResponse>();
        
        // Create multiple large messages
        for (int i = 0; i < messageSizes.Length; i++)
        {
            string content = new string((char)('A' + i), messageSizes[i]);
            var callToolResult = new CallToolResult
            {
                Content = [new TextContentBlock { Text = content }]
            };
            
            messages.Add(new JsonRpcResponse
            {
                Id = new RequestId(i + 1),
                Result = JsonSerializer.SerializeToNode(callToolResult, McpJsonUtilities.DefaultOptions)
            });
        }
        
        // Act - Send all messages
        foreach (var message in messages)
        {
            await transport.SendMessageAsync(message, CancellationToken.None);
        }
        
        // Assert - Verify all messages were written correctly
        outputStream.Position = 0;
        using var reader = new StreamReader(outputStream, Encoding.UTF8, leaveOpen: true);
        
        for (int i = 0; i < messages.Count; i++)
        {
            string? line = await reader.ReadLineAsync();
            Assert.NotNull(line);
            Assert.False(string.IsNullOrWhiteSpace(line));
            
            var deserializedMessage = JsonSerializer.Deserialize<JsonRpcResponse>(
                line, 
                McpJsonUtilities.DefaultOptions);
            
            Assert.NotNull(deserializedMessage);
            Assert.Equal(messages[i].Id, deserializedMessage.Id);
            
            var resultContent = deserializedMessage.Result?.Deserialize<CallToolResult>(McpJsonUtilities.DefaultOptions);
            Assert.NotNull(resultContent);
            var originalResult = messages[i].Result?.Deserialize<CallToolResult>(McpJsonUtilities.DefaultOptions);
            var originalContent = originalResult!.Content[0] as TextContentBlock;
            var deserializedContent = resultContent.Content[0] as TextContentBlock;
            Assert.Equal(originalContent!.Text, deserializedContent!.Text);
        }
    }

    /// <summary>
    /// Tests that the newline delimiter is correctly placed immediately after large messages.
    /// This ensures proper message framing in the JSON-RPC protocol.
    /// </summary>
    [Theory]
    [InlineData(20 * 1024)]
    [InlineData(64 * 1024)]
    public async Task SendMessageAsync_LargeMessage_NewlineImmediatelyFollowsJson(int messageSize)
    {
        // Arrange
        using var outputStream = new MemoryStream();
        using var inputStream = new MemoryStream();
        
        await using var transport = new StreamServerTransport(inputStream, outputStream);
        
        string largeContent = new string('Y', messageSize);
        var callToolResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = largeContent }]
        };
        
        var message = new JsonRpcResponse
        {
            Id = new RequestId(42),
            Result = JsonSerializer.SerializeToNode(callToolResult, McpJsonUtilities.DefaultOptions)
        };
        
        // Act
        await transport.SendMessageAsync(message, CancellationToken.None);
        
        // Assert
        byte[] writtenBytes = outputStream.ToArray();
        
        // Find the last '}' (end of JSON object)
        int lastBraceIndex = -1;
        for (int i = writtenBytes.Length - 1; i >= 0; i--)
        {
            if (writtenBytes[i] == (byte)'}')
            {
                lastBraceIndex = i;
                break;
            }
        }
        
        Assert.True(lastBraceIndex >= 0, "Could not find closing brace in message");
        
        // Verify that the next byte after the closing brace is the newline
        Assert.Equal(lastBraceIndex + 1, writtenBytes.Length - 1);
        Assert.Equal((byte)'\n', writtenBytes[lastBraceIndex + 1]);
    }

    /// <summary>
    /// Tests that error responses with large content are also handled correctly.
    /// </summary>
    [Fact]
    public async Task SendMessageAsync_LargeErrorMessage_DeliversSuccessfully()
    {
        // Arrange
        using var outputStream = new MemoryStream();
        using var inputStream = new MemoryStream();
        
        await using var transport = new StreamServerTransport(inputStream, outputStream);
        
        string largeErrorData = new string('E', 25 * 1024);
        var message = new JsonRpcError
        {
            Id = new RequestId(999),
            Error = new JsonRpcErrorDetail
            {
                Code = (int)McpErrorCode.InternalError,
                Message = "Large error occurred",
                Data = JsonSerializer.SerializeToNode(largeErrorData, McpJsonUtilities.DefaultOptions)
            }
        };
        
        // Act
        await transport.SendMessageAsync(message, CancellationToken.None);
        
        // Assert
        outputStream.Position = 0;
        byte[] writtenBytes = outputStream.ToArray();
        
        Assert.True(writtenBytes.Length > 0);
        Assert.Equal((byte)'\n', writtenBytes[^1]);
        
        string messageJson = Encoding.UTF8.GetString(writtenBytes, 0, writtenBytes.Length - 1);
        var deserializedMessage = JsonSerializer.Deserialize<JsonRpcError>(
            messageJson, 
            McpJsonUtilities.DefaultOptions);
        
        Assert.NotNull(deserializedMessage);
        Assert.Equal(message.Id, deserializedMessage.Id);
        Assert.NotNull(deserializedMessage.Error);
        var errorData = deserializedMessage.Error.Data?.Deserialize<string>(McpJsonUtilities.DefaultOptions);
        Assert.Equal(largeErrorData, errorData);
    }

    /// <summary>
    /// Tests that notification messages with large content work correctly.
    /// Notifications don't have an ID but should still handle large content properly.
    /// </summary>
    [Fact]
    public async Task SendMessageAsync_LargeNotification_DeliversSuccessfully()
    {
        // Arrange
        using var outputStream = new MemoryStream();
        using var inputStream = new MemoryStream();
        
        await using var transport = new StreamServerTransport(inputStream, outputStream);
        
        string largeLogContent = new string('L', 40 * 1024);
        var loggingParams = new LoggingMessageNotificationParams
        {
            Level = LoggingLevel.Info,
            Logger = "test-logger",
            Data = JsonSerializer.SerializeToElement(largeLogContent, McpJsonUtilities.DefaultOptions)
        };
        
        var message = new JsonRpcNotification
        {
            Method = NotificationMethods.LoggingMessage,
            Params = JsonSerializer.SerializeToNode(loggingParams, McpJsonUtilities.DefaultOptions)
        };
        
        // Act
        await transport.SendMessageAsync(message, CancellationToken.None);
        
        // Assert
        outputStream.Position = 0;
        byte[] writtenBytes = outputStream.ToArray();
        
        Assert.True(writtenBytes.Length > 0);
        Assert.Equal((byte)'\n', writtenBytes[^1]);
        
        string messageJson = Encoding.UTF8.GetString(writtenBytes, 0, writtenBytes.Length - 1);
        var deserializedMessage = JsonSerializer.Deserialize<JsonRpcNotification>(
            messageJson,
            McpJsonUtilities.DefaultOptions);
        
        Assert.NotNull(deserializedMessage);
        Assert.NotNull(deserializedMessage.Params);
        var loggingParams = deserializedMessage.Params.Deserialize<LoggingMessageNotificationParams>(McpJsonUtilities.DefaultOptions);
        Assert.NotNull(loggingParams);
        var logData = loggingParams.Data?.Deserialize<string>(McpJsonUtilities.DefaultOptions);
        Assert.Equal(largeLogContent, logData);
    }

    /// <summary>
    /// Tests edge case of exactly 16KB message (typical buffer size).
    /// This was the boundary where the original issue would manifest.
    /// </summary>
    [Fact]
    public async Task SendMessageAsync_ExactlyBufferSizedMessage_HandledCorrectly()
    {
        // Arrange
        using var outputStream = new MemoryStream();
        using var inputStream = new MemoryStream();
        
        await using var transport = new StreamServerTransport(inputStream, outputStream);
        
        // Create a message that serializes to approximately 16KB
        // Account for JSON structure overhead
        int targetSize = 16 * 1024;
        int overhead = 200; // Approximate JSON structure overhead
        string content = new string('Z', targetSize - overhead);
        
        var callToolResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = content }]
        };
        
        var message = new JsonRpcResponse
        {
            Id = new RequestId(1),
            Result = JsonSerializer.SerializeToNode(callToolResult, McpJsonUtilities.DefaultOptions)
        };
        
        // Act
        await transport.SendMessageAsync(message, CancellationToken.None);
        
        // Assert
        outputStream.Position = 0;
        byte[] writtenBytes = outputStream.ToArray();
        
        // Verify message was written and ends with newline
        Assert.True(writtenBytes.Length > 0);
        Assert.Equal((byte)'\n', writtenBytes[^1]);
        
        // Verify it's valid JSON and can be deserialized
        string messageJson = Encoding.UTF8.GetString(writtenBytes, 0, writtenBytes.Length - 1);
        var deserializedMessage = JsonSerializer.Deserialize<JsonRpcResponse>(
            messageJson, 
            McpJsonUtilities.DefaultOptions);
        
        Assert.NotNull(deserializedMessage);
        Assert.Equal(message.Id, deserializedMessage.Id);
    }

    /// <summary>
    /// Tests that very large messages (>100KB) are handled without performance issues or timeouts.
    /// </summary>
    [Fact(Timeout = 5000)] // 5 second timeout to catch any hanging
    public async Task SendMessageAsync_VeryLargeMessage_CompletesWithoutTimeout()
    {
        // Arrange
        using var outputStream = new MemoryStream();
        using var inputStream = new MemoryStream();
        
        await using var transport = new StreamServerTransport(inputStream, outputStream);
        
        // Create a 200KB message
        string veryLargeContent = new string('M', 200 * 1024);
        var callToolResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = veryLargeContent }]
        };
        
        var message = new JsonRpcResponse
        {
            Id = new RequestId(1000),
            Result = JsonSerializer.SerializeToNode(callToolResult, McpJsonUtilities.DefaultOptions)
        };
        
        // Act
        await transport.SendMessageAsync(message, CancellationToken.None);
        
        // Assert
        outputStream.Position = 0;
        byte[] writtenBytes = outputStream.ToArray();
        
        Assert.True(writtenBytes.Length > 200_000, "Message should be at least 200KB");
        Assert.Equal((byte)'\n', writtenBytes[^1]);
        
        // Verify content integrity
        string messageJson = Encoding.UTF8.GetString(writtenBytes, 0, writtenBytes.Length - 1);
        var deserializedMessage = JsonSerializer.Deserialize<JsonRpcResponse>(
            messageJson, 
            McpJsonUtilities.DefaultOptions);
        
        Assert.NotNull(deserializedMessage);
        var resultContent = deserializedMessage.Result?.Deserialize<CallToolResult>(McpJsonUtilities.DefaultOptions);
        Assert.NotNull(resultContent);
        var textContent = resultContent.Content[0] as TextContentBlock;
        Assert.Equal(veryLargeContent.Length, textContent!.Text!.Length);
    }
}