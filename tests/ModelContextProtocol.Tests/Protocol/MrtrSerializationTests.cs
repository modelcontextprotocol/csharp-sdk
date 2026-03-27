using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class MrtrSerializationTests
{
    [Fact]
    public static void IncompleteResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new IncompleteResult
        {
            InputRequests = new Dictionary<string, InputRequest>
            {
                ["input_1"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = "What is your name?",
                    RequestedSchema = new()
                }),
                ["input_2"] = InputRequest.ForSampling(new CreateMessageRequestParams
                {
                    Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Hello" }] }],
                    MaxTokens = 100
                })
            },
            RequestState = "correlation-123",
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<IncompleteResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("incomplete", deserialized.ResultType);
        Assert.Equal("correlation-123", deserialized.RequestState);
        Assert.NotNull(deserialized.InputRequests);
        Assert.Equal(2, deserialized.InputRequests.Count);
        Assert.True(deserialized.InputRequests.ContainsKey("input_1"));
        Assert.True(deserialized.InputRequests.ContainsKey("input_2"));
    }

    [Fact]
    public static void IncompleteResult_HasResultTypeIncomplete()
    {
        var result = new IncompleteResult();
        Assert.Equal("incomplete", result.ResultType);
    }

    [Fact]
    public static void IncompleteResult_ResultType_AppearsInJson()
    {
        var result = new IncompleteResult
        {
            RequestState = "abc",
        };

        string json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json);

        Assert.NotNull(node);
        Assert.Equal("incomplete", (string?)node["result_type"]);
        Assert.Equal("abc", (string?)node["requestState"]);
    }

    [Fact]
    public static void InputRequest_ForElicitation_SerializesCorrectly()
    {
        var inputRequest = InputRequest.ForElicitation(new ElicitRequestParams
        {
            Message = "Enter name",
            RequestedSchema = new()
        });

        string json = JsonSerializer.Serialize(inputRequest, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json);

        Assert.NotNull(node);
        Assert.Equal("elicitation/create", (string?)node["method"]);
        Assert.NotNull(node["params"]);
        Assert.Equal("Enter name", (string?)node["params"]!["message"]);
    }

    [Fact]
    public static void InputRequest_ForSampling_SerializesCorrectly()
    {
        var inputRequest = InputRequest.ForSampling(new CreateMessageRequestParams
        {
            Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Prompt" }] }],
            MaxTokens = 50
        });

        string json = JsonSerializer.Serialize(inputRequest, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json);

        Assert.NotNull(node);
        Assert.Equal("sampling/createMessage", (string?)node["method"]);
        Assert.NotNull(node["params"]);
        Assert.Equal(50, (int?)node["params"]!["maxTokens"]);
    }

    [Fact]
    public static void InputRequest_ForRootsList_SerializesCorrectly()
    {
        var inputRequest = InputRequest.ForRootsList(new ListRootsRequestParams());

        string json = JsonSerializer.Serialize(inputRequest, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json);

        Assert.NotNull(node);
        Assert.Equal("roots/list", (string?)node["method"]);
    }

    [Fact]
    public static void InputRequest_Elicitation_RoundTrip()
    {
        var original = InputRequest.ForElicitation(new ElicitRequestParams
        {
            Message = "test message",
            RequestedSchema = new()
        });

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InputRequest>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("elicitation/create", deserialized.Method);
        Assert.NotNull(deserialized.ElicitationParams);
        Assert.Equal("test message", deserialized.ElicitationParams.Message);
    }

    [Fact]
    public static void InputRequest_Sampling_RoundTrip()
    {
        var original = InputRequest.ForSampling(new CreateMessageRequestParams
        {
            Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Hello" }] }],
            MaxTokens = 200
        });

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InputRequest>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("sampling/createMessage", deserialized.Method);
        Assert.NotNull(deserialized.SamplingParams);
        Assert.Equal(200, deserialized.SamplingParams.MaxTokens);
    }

    [Fact]
    public static void InputRequest_RootsList_RoundTrip()
    {
        var original = InputRequest.ForRootsList(new ListRootsRequestParams());

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InputRequest>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("roots/list", deserialized.Method);
        Assert.NotNull(deserialized.RootsParams);
    }

    [Fact]
    public static void InputResponse_FromSamplingResult_RoundTrip()
    {
        var samplingResult = new CreateMessageResult
        {
            Content = [new TextContentBlock { Text = "Response text" }],
            Model = "test-model"
        };

        var inputResponse = InputResponse.FromSamplingResult(samplingResult);

        // Serialize → deserialize
        string json = JsonSerializer.Serialize(inputResponse, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InputResponse>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.SamplingResult);
        Assert.Equal("test-model", deserialized.SamplingResult.Model);
    }

    [Fact]
    public static void InputResponse_FromElicitResult_RoundTrip()
    {
        var elicitResult = new ElicitResult
        {
            Action = "confirm",
            Content = new Dictionary<string, JsonElement>
            {
                ["key"] = JsonDocument.Parse("\"value\"").RootElement.Clone()
            }
        };

        var inputResponse = InputResponse.FromElicitResult(elicitResult);

        string json = JsonSerializer.Serialize(inputResponse, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InputResponse>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.ElicitationResult);
        Assert.Equal("confirm", deserialized.ElicitationResult.Action);
    }

    [Fact]
    public static void InputResponse_FromRootsResult_RoundTrip()
    {
        var rootsResult = new ListRootsResult
        {
            Roots = [new Root { Uri = "file:///test", Name = "Test" }]
        };

        var inputResponse = InputResponse.FromRootsResult(rootsResult);

        string json = JsonSerializer.Serialize(inputResponse, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InputResponse>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.RootsResult);
        Assert.Single(deserialized.RootsResult.Roots);
        Assert.Equal("file:///test", deserialized.RootsResult.Roots[0].Uri);
    }

    [Fact]
    public static void InputRequestDictionary_SerializationRoundTrip()
    {
        IDictionary<string, InputRequest> requests = new Dictionary<string, InputRequest>
        {
            ["a"] = InputRequest.ForElicitation(new ElicitRequestParams { Message = "q1", RequestedSchema = new() }),
            ["b"] = InputRequest.ForSampling(new CreateMessageRequestParams
            {
                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "q2" }] }],
                MaxTokens = 50
            }),
        };

        string json = JsonSerializer.Serialize(requests, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<IDictionary<string, InputRequest>>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Count);
        Assert.Equal("elicitation/create", deserialized["a"].Method);
        Assert.Equal("sampling/createMessage", deserialized["b"].Method);
    }

    [Fact]
    public static void InputResponseDictionary_SerializationRoundTrip()
    {
        IDictionary<string, InputResponse> responses = new Dictionary<string, InputResponse>
        {
            ["a"] = InputResponse.FromElicitResult(new ElicitResult { Action = "confirm" }),
            ["b"] = InputResponse.FromSamplingResult(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = "AI" }],
                Model = "m1"
            }),
        };

        string json = JsonSerializer.Serialize(responses, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<IDictionary<string, InputResponse>>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Count);
    }

    [Fact]
    public static void Result_ResultType_DefaultsToNull()
    {
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "test" }]
        };

        string json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json);

        // result_type should not appear for normal results
        Assert.Null(node?["result_type"]);
    }

    [Fact]
    public static void RequestParams_InputResponses_NotSerializedByDefault()
    {
        var callParams = new CallToolRequestParams
        {
            Name = "test-tool",
        };

        string json = JsonSerializer.Serialize(callParams, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json);

        // inputResponses and requestState should not appear when null
        Assert.Null(node?["inputResponses"]);
        Assert.Null(node?["requestState"]);
    }
}
