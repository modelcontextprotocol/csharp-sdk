using BenchmarkDotNet.Attributes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

public class JsonRpcMessageDeserializationBenchmarks
{
    private byte[] _requestJson = null!;
    private byte[] _notificationJson = null!;
    private byte[] _responseJson = null!;
    private byte[] _errorJson = null!;
    private JsonSerializerOptions _options = null!;

    [GlobalSetup]
    public void Setup()
    {
        _options = McpJsonUtilities.DefaultOptions;

        _requestJson = JsonSerializer.SerializeToUtf8Bytes(
            new JsonRpcRequest
            {
                Id = new RequestId("1"),
                Method = "test",
                Params = JsonValue.Create(1)
            },
            _options);

        _notificationJson = JsonSerializer.SerializeToUtf8Bytes(
            new JsonRpcNotification
            {
                Method = "notify",
                Params = JsonValue.Create(2)
            },
            _options);

        _responseJson = JsonSerializer.SerializeToUtf8Bytes(
            new JsonRpcResponse
            {
                Id = new RequestId("1"),
                Result = JsonValue.Create(3)
            },
            _options);

        _errorJson = JsonSerializer.SerializeToUtf8Bytes(
            new JsonRpcError
            {
                Id = new RequestId("1"),
                Error = new JsonRpcErrorDetail { Code = 42, Message = "oops" }
            },
            _options);
    }

    [Benchmark]
    public JsonRpcMessage DeserializeRequest() =>
        JsonSerializer.Deserialize<JsonRpcMessage>(_requestJson, _options)!;

    [Benchmark]
    public JsonRpcMessage DeserializeNotification() =>
        JsonSerializer.Deserialize<JsonRpcMessage>(_notificationJson, _options)!;

    [Benchmark]
    public JsonRpcMessage DeserializeResponse() =>
        JsonSerializer.Deserialize<JsonRpcMessage>(_responseJson, _options)!;

    [Benchmark]
    public JsonRpcMessage DeserializeError() =>
        JsonSerializer.Deserialize<JsonRpcMessage>(_errorJson, _options)!;
}
