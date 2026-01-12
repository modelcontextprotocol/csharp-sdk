using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Tests for the optimized JsonRpcMessage.Converter implementation.
/// </summary>
public static class JsonRpcMessageConverterTests
{
    [Fact]
    public static void Deserialize_JsonRpcRequest_WithAllProperties()
    {
        // Arrange
        string json = """{"jsonrpc":"2.0","id":123,"method":"test/method","params":{"key":"value"}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal("2.0", request.JsonRpc);
        Assert.Equal(new RequestId(123), request.Id);
        Assert.Equal("test/method", request.Method);
        Assert.NotNull(request.Params);
        Assert.Equal("value", request.Params["key"]?.GetValue<string>());
    }

    [Fact]
    public static void Deserialize_JsonRpcRequest_WithStringId()
    {
        // Arrange
        string json = """{"jsonrpc":"2.0","id":"abc-123","method":"test/method"}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal("2.0", request.JsonRpc);
        Assert.Equal(new RequestId("abc-123"), request.Id);
        Assert.Equal("test/method", request.Method);
    }

    [Fact]
    public static void Deserialize_JsonRpcNotification_WithParams()
    {
        // Arrange
        string json = """{"jsonrpc":"2.0","method":"notifications/progress","params":{"progress":50}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcNotification>(message);
        var notification = (JsonRpcNotification)message;
        Assert.Equal("2.0", notification.JsonRpc);
        Assert.Equal("notifications/progress", notification.Method);
        Assert.NotNull(notification.Params);
        Assert.Equal(50, notification.Params["progress"]?.GetValue<int>());
    }

    [Fact]
    public static void Deserialize_JsonRpcResponse_WithResult()
    {
        // Arrange
        string json = """{"jsonrpc":"2.0","id":42,"result":{"status":"success","data":[1,2,3]}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcResponse>(message);
        var response = (JsonRpcResponse)message;
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal(new RequestId(42), response.Id);
        Assert.NotNull(response.Result);
        Assert.Equal("success", response.Result["status"]?.GetValue<string>());
    }

    [Fact]
    public static void Deserialize_JsonRpcResponse_WithNullResult()
    {
        // Arrange
        string json = """{"jsonrpc":"2.0","id":1,"result":null}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcResponse>(message);
        var response = (JsonRpcResponse)message;
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal(new RequestId(1), response.Id);
        Assert.Null(response.Result);
    }

    [Fact]
    public static void Deserialize_JsonRpcError_WithErrorDetails()
    {
        // Arrange
        string json = """{"jsonrpc":"2.0","id":"req-1","error":{"code":-32600,"message":"Invalid Request","data":"Additional error info"}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcError>(message);
        var error = (JsonRpcError)message;
        Assert.Equal("2.0", error.JsonRpc);
        Assert.Equal(new RequestId("req-1"), error.Id);
        Assert.NotNull(error.Error);
        Assert.Equal(-32600, error.Error.Code);
        Assert.Equal("Invalid Request", error.Error.Message);
        Assert.Equal("Additional error info", error.Error.Data?.ToString());
    }

    [Fact]
    public static void Deserialize_JsonRpcMessage_IgnoresUnknownProperties()
    {
        // Arrange - JSON with unknown properties
        string json = """{"jsonrpc":"2.0","id":1,"method":"test","params":{},"extra":"ignored","another":123}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert - should successfully deserialize, ignoring unknown properties
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal("test", request.Method);
    }

    [Fact]
    public static void Deserialize_InvalidJsonRpcVersion_ThrowsException()
    {
        // Arrange
        string json = """{"jsonrpc":"1.0","id":1,"method":"test"}""";

        // Act & Assert
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions));
        Assert.Contains("jsonrpc version", exception.Message);
    }

    [Fact]
    public static void Deserialize_MissingJsonRpcVersion_ThrowsException()
    {
        // Arrange
        string json = """{"id":1,"method":"test"}""";

        // Act & Assert
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions));
        Assert.Contains("jsonrpc version", exception.Message);
    }

    [Fact]
    public static void Deserialize_ResponseWithoutResultOrError_ThrowsException()
    {
        // Arrange
        string json = """{"jsonrpc":"2.0","id":1}""";

        // Act & Assert
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions));
        Assert.Contains("result or error", exception.Message);
    }

    [Fact]
    public static void Deserialize_InvalidMessageFormat_ThrowsException()
    {
        // Arrange - neither request nor response nor notification
        string json = """{"jsonrpc":"2.0"}""";

        // Act & Assert
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions));
        Assert.Contains("Invalid JSON-RPC message format", exception.Message);
    }

    [Fact]
    public static void Serialize_JsonRpcRequest_ProducesCorrectJson()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = new RequestId(123),
            Method = "test/method",
            Params = JsonNode.Parse("""{"key":"value"}""")
        };

        // Act
        string json = JsonSerializer.Serialize<JsonRpcMessage>(request, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.Contains("\"jsonrpc\":\"2.0\"", json);
        Assert.Contains("\"id\":123", json);
        Assert.Contains("\"method\":\"test/method\"", json);
        Assert.Contains("\"key\":\"value\"", json);
    }

    [Fact]
    public static void Serialize_JsonRpcNotification_ProducesCorrectJson()
    {
        // Arrange
        var notification = new JsonRpcNotification
        {
            JsonRpc = "2.0",
            Method = "notifications/test"
        };

        // Act
        string json = JsonSerializer.Serialize<JsonRpcMessage>(notification, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.Contains("\"jsonrpc\":\"2.0\"", json);
        Assert.Contains("\"method\":\"notifications/test\"", json);
        Assert.DoesNotContain("\"id\"", json);
    }

    [Fact]
    public static void RoundTrip_Request_PreservesData()
    {
        // Arrange
        var original = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = new RequestId("test-id"),
            Method = "some/method",
            Params = JsonNode.Parse("""{"nested":{"value":42}}""")
        };

        // Act
        string json = JsonSerializer.Serialize<JsonRpcMessage>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions) as JsonRpcRequest;

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.JsonRpc, deserialized.JsonRpc);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Method, deserialized.Method);
        Assert.Equal(42, deserialized.Params?["nested"]?["value"]?.GetValue<int>());
    }

    [Fact]
    public static void RoundTrip_Response_PreservesData()
    {
        // Arrange
        var original = new JsonRpcResponse
        {
            JsonRpc = "2.0",
            Id = new RequestId(999),
            Result = JsonNode.Parse("""{"success":true,"items":[1,2,3]}""")
        };

        // Act
        string json = JsonSerializer.Serialize<JsonRpcMessage>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions) as JsonRpcResponse;

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.JsonRpc, deserialized.JsonRpc);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.True(deserialized.Result?["success"]?.GetValue<bool>());
    }

    [Fact]
    public static void RoundTrip_Error_PreservesData()
    {
        // Arrange
        var original = new JsonRpcError
        {
            JsonRpc = "2.0",
            Id = new RequestId(100),
            Error = new JsonRpcErrorDetail
            {
                Code = -32601,
                Message = "Method not found",
                Data = "test/unknown"
            }
        };

        // Act
        string json = JsonSerializer.Serialize<JsonRpcMessage>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions) as JsonRpcError;

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.JsonRpc, deserialized.JsonRpc);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Error.Code, deserialized.Error.Code);
        Assert.Equal(original.Error.Message, deserialized.Error.Message);
    }
}
