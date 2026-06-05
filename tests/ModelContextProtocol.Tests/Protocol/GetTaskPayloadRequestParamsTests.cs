using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class GetTaskPayloadRequestParamsTests
{
    [Fact]
    public static void GetTaskPayloadRequestParams_SerializationRoundTrip()
    {
        // Arrange
        var original = new GetTaskPayloadRequestParams
        {
            TaskId = "payload-task-999"
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetTaskPayloadRequestParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
    }
}
