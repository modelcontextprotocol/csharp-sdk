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

    [Fact]
    public static void Deserialize_ResponseWithExplicitNullError_TreatedAsSuccessResponse()
    {
        // Arrange - Some implementations may include "error": null in success responses.
        // While JSON-RPC 2.0 spec says responses have either result OR error (not both),
        // this tests that we handle the lenient case gracefully.
        string json = """{"jsonrpc":"2.0","id":1,"result":{"data":"value"},"error":null}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert - Should be a success response since error is null
        Assert.NotNull(message);
        Assert.IsType<JsonRpcResponse>(message);
        var response = (JsonRpcResponse)message;
        Assert.Equal(new RequestId(1), response.Id);
        Assert.NotNull(response.Result);
        Assert.Equal("value", response.Result["data"]?.GetValue<string>());
    }

    [Fact]
    public static void Deserialize_ResponseWithNullResultAndNullError_TreatedAsSuccessWithNullResult()
    {
        // Arrange - Both result and error are explicitly null.
        // Per JSON-RPC 2.0, result: null is a valid success response value.
        string json = """{"jsonrpc":"2.0","id":1,"result":null,"error":null}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert - result: null is valid, error: null is ignored
        Assert.NotNull(message);
        Assert.IsType<JsonRpcResponse>(message);
        var response = (JsonRpcResponse)message;
        Assert.Equal(new RequestId(1), response.Id);
        Assert.Null(response.Result);
    }

    [Fact]
    public static void Deserialize_ResponseWithBothErrorAndResult_ErrorTakesPrecedence()
    {
        // Arrange - JSON-RPC 2.0 spec says a response should have either result OR error, not both.
        // However, if a non-compliant implementation sends both, we verify consistent behavior:
        // error takes precedence regardless of property order.
        string json = """{"jsonrpc":"2.0","id":1,"error":{"code":-32600,"message":"Invalid"},"result":{"data":"ignored"}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert - Error takes precedence
        Assert.NotNull(message);
        Assert.IsType<JsonRpcError>(message);
        var error = (JsonRpcError)message;
        Assert.Equal(new RequestId(1), error.Id);
        Assert.Equal(-32600, error.Error.Code);
    }

    [Fact]
    public static void Deserialize_ResponseWithBothResultAndError_ErrorTakesPrecedenceRegardlessOfOrder()
    {
        // Arrange - Same as above but with result appearing before error in the JSON.
        // Validates that property order doesn't affect the precedence logic.
        string json = """{"jsonrpc":"2.0","id":1,"result":{"data":"ignored"},"error":{"code":-32600,"message":"Invalid"}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert - Error still takes precedence
        Assert.NotNull(message);
        Assert.IsType<JsonRpcError>(message);
        var error = (JsonRpcError)message;
        Assert.Equal(new RequestId(1), error.Id);
        Assert.Equal(-32600, error.Error.Code);
    }

    [Fact]
    public static void Deserialize_RequestWithEmptyStringId_IsValidRequest()
    {
        // Arrange - Empty string is a valid ID per JSON-RPC 2.0
        string json = """{"jsonrpc":"2.0","id":"","method":"test"}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal(new RequestId(""), request.Id);
        Assert.Equal("test", request.Method);
    }

    [Fact]
    public static void Deserialize_RequestWithZeroId_IsValidRequest()
    {
        // Arrange - Zero is a valid numeric ID
        string json = """{"jsonrpc":"2.0","id":0,"method":"test"}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal(new RequestId(0), request.Id);
    }

    [Fact]
    public static void Deserialize_RequestWithNegativeId_IsValidRequest()
    {
        // Arrange - Negative numbers are valid IDs
        string json = """{"jsonrpc":"2.0","id":-42,"method":"test"}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal(new RequestId(-42), request.Id);
    }

    [Fact]
    public static void Deserialize_RequestWithLargeNumericId_IsValidRequest()
    {
        // Arrange - Large number ID
        string json = """{"jsonrpc":"2.0","id":9223372036854775807,"method":"test"}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal(new RequestId(long.MaxValue), request.Id);
    }

    [Fact]
    public static void Deserialize_NotificationWithExplicitNullParams_IsValidNotification()
    {
        // Arrange - params: null is valid
        string json = """{"jsonrpc":"2.0","method":"notify","params":null}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcNotification>(message);
        var notification = (JsonRpcNotification)message;
        Assert.Equal("notify", notification.Method);
        Assert.Null(notification.Params);
    }

    [Fact]
    public static void Deserialize_RequestWithEmptyObjectParams_IsValidRequest()
    {
        // Arrange - Empty object params
        string json = """{"jsonrpc":"2.0","id":1,"method":"test","params":{}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.NotNull(request.Params);
        Assert.IsType<JsonObject>(request.Params);
    }

    [Fact]
    public static void Deserialize_RequestWithArrayParams_IsValidRequest()
    {
        // Arrange - Array params (positional arguments per JSON-RPC 2.0)
        string json = """{"jsonrpc":"2.0","id":1,"method":"test","params":["arg1",42,true]}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.NotNull(request.Params);
        Assert.IsType<JsonArray>(request.Params);
        var array = (JsonArray)request.Params;
        Assert.Equal(3, array.Count);
        Assert.Equal("arg1", array[0]?.GetValue<string>());
        Assert.Equal(42, array[1]?.GetValue<int>());
        Assert.True(array[2]?.GetValue<bool>());
    }

    [Fact]
    public static void Deserialize_ErrorWithNullData_IsValidError()
    {
        // Arrange - Error with explicit null data
        string json = """{"jsonrpc":"2.0","id":1,"error":{"code":-32600,"message":"Invalid","data":null}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcError>(message);
        var error = (JsonRpcError)message;
        Assert.Equal(-32600, error.Error.Code);
        Assert.Equal("Invalid", error.Error.Message);
        Assert.Null(error.Error.Data);
    }

    [Fact]
    public static void Deserialize_ErrorWithComplexData_IsValidError()
    {
        // Arrange - Error with complex object data
        string json = """{"jsonrpc":"2.0","id":1,"error":{"code":-32600,"message":"Invalid","data":{"details":["error1","error2"],"field":"name"}}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcError>(message);
        var error = (JsonRpcError)message;
        Assert.NotNull(error.Error.Data);
    }

    [Fact]
    public static void Deserialize_RequestWithPropertiesInUnusualOrder_IsValidRequest()
    {
        // Arrange - Properties in unusual order (params, method, id, jsonrpc)
        string json = """{"params":{"key":"value"},"method":"test","id":123,"jsonrpc":"2.0"}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal("2.0", request.JsonRpc);
        Assert.Equal(new RequestId(123), request.Id);
        Assert.Equal("test", request.Method);
        Assert.Equal("value", request.Params?["key"]?.GetValue<string>());
    }

    [Fact]
    public static void Deserialize_ResponseWithPropertiesInUnusualOrder_IsValidResponse()
    {
        // Arrange - Properties in unusual order (result, id, jsonrpc)
        string json = """{"result":{"status":"ok"},"id":"abc","jsonrpc":"2.0"}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcResponse>(message);
        var response = (JsonRpcResponse)message;
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal(new RequestId("abc"), response.Id);
        Assert.Equal("ok", response.Result?["status"]?.GetValue<string>());
    }

    [Fact]
    public static void Deserialize_MessageWithUnicodeInStringValues_PreservesUnicode()
    {
        // Arrange - Unicode characters in method name, ID, and params
        string json = """{"jsonrpc":"2.0","id":"请求-123","method":"日本語/メソッド","params":{"emoji":"🚀","text":"Ελληνικά"}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal(new RequestId("请求-123"), request.Id);
        Assert.Equal("日本語/メソッド", request.Method);
        Assert.Equal("🚀", request.Params?["emoji"]?.GetValue<string>());
        Assert.Equal("Ελληνικά", request.Params?["text"]?.GetValue<string>());
    }

    [Fact]
    public static void Deserialize_MessageWithEscapedCharacters_HandlesEscaping()
    {
        // Arrange - JSON with escaped characters
        string json = """{"jsonrpc":"2.0","id":1,"method":"test","params":{"path":"C:\\Users\\test","quote":"He said \"hello\""}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal("C:\\Users\\test", request.Params?["path"]?.GetValue<string>());
        Assert.Equal("He said \"hello\"", request.Params?["quote"]?.GetValue<string>());
    }

    [Fact]
    public static void Deserialize_ResponseWithPrimitiveResult_IsValid()
    {
        // Arrange - Result is a primitive string, not an object
        string json = """{"jsonrpc":"2.0","id":1,"result":"simple string result"}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcResponse>(message);
        var response = (JsonRpcResponse)message;
        Assert.NotNull(response.Result);
        Assert.Equal("simple string result", response.Result.GetValue<string>());
    }

    [Fact]
    public static void Deserialize_ResponseWithNumericResult_IsValid()
    {
        // Arrange - Result is a number
        string json = """{"jsonrpc":"2.0","id":1,"result":42}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcResponse>(message);
        var response = (JsonRpcResponse)message;
        Assert.NotNull(response.Result);
        Assert.Equal(42, response.Result.GetValue<int>());
    }

    [Fact]
    public static void Deserialize_ResponseWithBooleanResult_IsValid()
    {
        // Arrange - Result is a boolean
        string json = """{"jsonrpc":"2.0","id":1,"result":true}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcResponse>(message);
        var response = (JsonRpcResponse)message;
        Assert.NotNull(response.Result);
        Assert.True(response.Result.GetValue<bool>());
    }

    [Fact]
    public static void Deserialize_ResponseWithArrayResult_IsValid()
    {
        // Arrange - Result is an array
        string json = """{"jsonrpc":"2.0","id":1,"result":[1,2,3,"four"]}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcResponse>(message);
        var response = (JsonRpcResponse)message;
        Assert.NotNull(response.Result);
        Assert.IsType<JsonArray>(response.Result);
        var array = (JsonArray)response.Result;
        Assert.Equal(4, array.Count);
    }

    [Fact]
    public static void Deserialize_MessageWithMultipleUnknownPropertiesInterspersed_IgnoresUnknown()
    {
        // Arrange - Unknown properties interspersed with known ones
        string json = """{"unknown1":"x","jsonrpc":"2.0","unknown2":123,"id":1,"unknown3":true,"method":"test","unknown4":null}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.Equal("2.0", request.JsonRpc);
        Assert.Equal(new RequestId(1), request.Id);
        Assert.Equal("test", request.Method);
    }

    [Fact]
    public static void Deserialize_NotificationWithMethodOnly_NoParams_IsValid()
    {
        // Arrange - Minimal notification with no params
        string json = """{"jsonrpc":"2.0","method":"ping"}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcNotification>(message);
        var notification = (JsonRpcNotification)message;
        Assert.Equal("ping", notification.Method);
        Assert.Null(notification.Params);
    }

    [Fact]
    public static void Deserialize_RequestWithNestedComplexParams_IsValid()
    {
        // Arrange - Deeply nested params structure
        string json = """{"jsonrpc":"2.0","id":1,"method":"test","params":{"level1":{"level2":{"level3":{"value":"deep"}}}}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcRequest>(message);
        var request = (JsonRpcRequest)message;
        Assert.NotNull(request.Params);
        var deepValue = request.Params["level1"]?["level2"]?["level3"]?["value"]?.GetValue<string>();
        Assert.Equal("deep", deepValue);
    }

    [Fact]
    public static void Deserialize_ErrorWithNumericData_IsValid()
    {
        // Arrange - Error with numeric data (not object or string)
        string json = """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"Error","data":42}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcError>(message);
        var error = (JsonRpcError)message;
        Assert.NotNull(error.Error.Data);
    }

    [Fact]
    public static void Deserialize_ErrorWithArrayData_IsValid()
    {
        // Arrange - Error with array data
        string json = """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"Multiple errors","data":["error1","error2","error3"]}}""";

        // Act
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(message);
        Assert.IsType<JsonRpcError>(message);
        var error = (JsonRpcError)message;
        Assert.NotNull(error.Error.Data);
    }
}
