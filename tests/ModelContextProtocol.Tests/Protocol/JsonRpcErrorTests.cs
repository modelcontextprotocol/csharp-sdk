using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class JsonRpcErrorTests
{
    [Fact]
    public static void JsonRpcError_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new JsonRpcError
        {
            Id = new RequestId(1),
            Error = new JsonRpcErrorDetail
            {
                Code = -32600,
                Message = "Invalid Request",
                Data = "Additional error context"
            }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        var error = Assert.IsType<JsonRpcError>(deserialized);
        Assert.Equal(original.Id, error.Id);
        Assert.Equal(-32600, error.Error.Code);
        Assert.Equal("Invalid Request", error.Error.Message);
        Assert.NotNull(error.Error.Data);
    }

    [Fact]
    public static void JsonRpcError_SerializationRoundTrip_WithoutOptionalData()
    {
        var original = new JsonRpcError
        {
            Id = new RequestId("err-42"),
            Error = new JsonRpcErrorDetail
            {
                Code = -32601,
                Message = "Method not found"
            }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        var error = Assert.IsType<JsonRpcError>(deserialized);
        Assert.Equal(original.Id, error.Id);
        Assert.Equal(-32601, error.Error.Code);
        Assert.Equal("Method not found", error.Error.Message);
    }
}
